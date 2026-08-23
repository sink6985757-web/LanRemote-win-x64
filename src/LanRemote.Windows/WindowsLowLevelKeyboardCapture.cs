using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LanRemote.Windows;

public readonly record struct CapturedPhysicalKey(
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtended,
    bool IsDown);

public enum PhysicalKeyboardDisposition
{
    PassThrough,
    ForwardRemote,
    LocalSecureAttention,
}

public sealed class PhysicalKeyboardRoutingState
{
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyLeftControl = 0xA2;
    private const ushort VirtualKeyRightControl = 0xA3;
    private const ushort VirtualKeyMenu = 0x12;
    private const ushort VirtualKeyLeftMenu = 0xA4;
    private const ushort VirtualKeyRightMenu = 0xA5;
    private const ushort VirtualKeyDelete = 0x2E;
    private readonly HashSet<ushort> _pressedKeys = [];
    private bool _localSecureAttentionActive;

    public PhysicalKeyboardDisposition Process(
        ushort virtualKey,
        bool isDown,
        bool isInjected,
        bool captureEnabled)
    {
        if (!captureEnabled)
        {
            Reset();
            return PhysicalKeyboardDisposition.PassThrough;
        }

        if (isInjected)
        {
            return PhysicalKeyboardDisposition.PassThrough;
        }

        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }

        if (!_localSecureAttentionActive &&
            isDown &&
            virtualKey == VirtualKeyDelete &&
            IsControlPressed() &&
            IsAltPressed())
        {
            _localSecureAttentionActive = true;
            return PhysicalKeyboardDisposition.LocalSecureAttention;
        }

        if (_localSecureAttentionActive)
        {
            if (!IsControlPressed() && !IsAltPressed() && !_pressedKeys.Contains(VirtualKeyDelete))
            {
                _localSecureAttentionActive = false;
            }

            return PhysicalKeyboardDisposition.PassThrough;
        }

        return PhysicalKeyboardDisposition.ForwardRemote;
    }

    public void Reset()
    {
        _pressedKeys.Clear();
        _localSecureAttentionActive = false;
    }

    private bool IsControlPressed() =>
        _pressedKeys.Contains(VirtualKeyControl) ||
        _pressedKeys.Contains(VirtualKeyLeftControl) ||
        _pressedKeys.Contains(VirtualKeyRightControl);

    private bool IsAltPressed() =>
        _pressedKeys.Contains(VirtualKeyMenu) ||
        _pressedKeys.Contains(VirtualKeyLeftMenu) ||
        _pressedKeys.Contains(VirtualKeyRightMenu);
}

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsLowLevelKeyboardCapture : IDisposable
{
    private const int HookKeyboardLowLevel = 13;
    private const int HookAction = 0;
    private const uint MessageKeyDown = 0x0100;
    private const uint MessageKeyUp = 0x0101;
    private const uint MessageSystemKeyDown = 0x0104;
    private const uint MessageSystemKeyUp = 0x0105;
    private const uint FlagExtended = 0x01;
    private const uint FlagLowerIntegrityInjected = 0x02;
    private const uint FlagInjected = 0x10;
    private readonly nint _targetWindowHandle;
    private readonly Action<CapturedPhysicalKey> _forwardRemote;
    private readonly Action _localSecureAttention;
    private readonly Action<Exception>? _captureFailed;
    private readonly PhysicalKeyboardRoutingState _routingState = new();
    private readonly LowLevelKeyboardProcedure _procedure;
    private nint _hookHandle;
    private bool _disposed;

    public WindowsLowLevelKeyboardCapture(
        nint targetWindowHandle,
        Action<CapturedPhysicalKey> forwardRemote,
        Action localSecureAttention,
        Action<Exception>? captureFailed = null)
    {
        if (targetWindowHandle == nint.Zero)
        {
            throw new ArgumentException("鍵盤擷取需要有效的目標視窗 handle。", nameof(targetWindowHandle));
        }

        _targetWindowHandle = targetWindowHandle;
        _forwardRemote = forwardRemote ?? throw new ArgumentNullException(nameof(forwardRemote));
        _localSecureAttention = localSecureAttention ?? throw new ArgumentNullException(nameof(localSecureAttention));
        _captureFailed = captureFailed;
        _procedure = HookCallback;
    }

    public bool IsInstalled => _hookHandle != nint.Zero;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsInstalled)
        {
            return;
        }

        _routingState.Reset();
        _hookHandle = SetWindowsHookExW(
            HookKeyboardLowLevel,
            _procedure,
            nint.Zero,
            0);
        if (_hookHandle == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows 無法啟用實體鍵盤擷取。遠端輸入將退回視窗內鍵盤事件。");
        }
    }

    public void Stop()
    {
        _routingState.Reset();
        nint hookHandle = _hookHandle;
        _hookHandle = nint.Zero;
        if (hookHandle == nint.Zero)
        {
            return;
        }

        if (!UnhookWindowsHookEx(hookHandle))
        {
            ReportFailure(new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows 無法解除實體鍵盤擷取。"));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int code, nint message, nint dataPointer)
    {
        if (code < HookAction)
        {
            return CallNextHookEx(_hookHandle, code, message, dataPointer);
        }

        try
        {
            uint keyboardMessage = unchecked((uint)message.ToInt64());
            bool isDown = keyboardMessage is MessageKeyDown or MessageSystemKeyDown;
            bool isUp = keyboardMessage is MessageKeyUp or MessageSystemKeyUp;
            if (!isDown && !isUp)
            {
                return CallNextHookEx(_hookHandle, code, message, dataPointer);
            }

            LowLevelKeyboardData data = Marshal.PtrToStructure<LowLevelKeyboardData>(dataPointer);
            bool isInjected = (data.Flags & (FlagInjected | FlagLowerIntegrityInjected)) != 0;
            bool captureEnabled = GetForegroundWindow() == _targetWindowHandle;
            PhysicalKeyboardDisposition disposition = _routingState.Process(
                (ushort)data.VirtualKey,
                isDown,
                isInjected,
                captureEnabled);

            if (disposition == PhysicalKeyboardDisposition.PassThrough)
            {
                return CallNextHookEx(_hookHandle, code, message, dataPointer);
            }

            if (disposition == PhysicalKeyboardDisposition.LocalSecureAttention)
            {
                _localSecureAttention();
                return CallNextHookEx(_hookHandle, code, message, dataPointer);
            }

            if (!TryCreatePhysicalKey(data, isDown, out CapturedPhysicalKey physicalKey))
            {
                return CallNextHookEx(_hookHandle, code, message, dataPointer);
            }

            _forwardRemote(physicalKey);
            return new nint(1);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return CallNextHookEx(_hookHandle, code, message, dataPointer);
        }
    }

    private static bool TryCreatePhysicalKey(
        LowLevelKeyboardData data,
        bool isDown,
        out CapturedPhysicalKey physicalKey)
    {
        physicalKey = default;
        if (data.VirtualKey is 0 or > ushort.MaxValue)
        {
            return false;
        }

        ushort scanCode;
        bool isExtended = (data.Flags & FlagExtended) != 0;
        if (data.ScanCode is > 0 and <= ushort.MaxValue)
        {
            scanCode = (ushort)data.ScanCode;
        }
        else if (WindowsKeyboardMapper.TryMapVirtualKey(
                     (int)data.VirtualKey,
                     out PhysicalKeyDescriptor mapped))
        {
            scanCode = mapped.ScanCode;
            isExtended = mapped.IsExtended;
        }
        else
        {
            return false;
        }

        physicalKey = new CapturedPhysicalKey(
            (ushort)data.VirtualKey,
            scanCode,
            isExtended,
            isDown);
        return true;
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _captureFailed?.Invoke(exception);
        }
        catch
        {
            // Never allow application error reporting to escape a native hook callback.
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint LowLevelKeyboardProcedure(int code, nint message, nint dataPointer);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(
        int hookType,
        LowLevelKeyboardProcedure procedure,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hookHandle,
        int code,
        nint message,
        nint dataPointer);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
