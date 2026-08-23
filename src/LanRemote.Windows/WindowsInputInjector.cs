using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Windows;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsInputInjector : IInputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseAbsolute = 0x8000;
    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const uint KeyScanCode = 0x0008;
    private const ushort VirtualKeyLeftControl = 0xA2;
    private const ushort VirtualKeyRightControl = 0xA3;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyLeftMenu = 0xA4;
    private const ushort VirtualKeyRightMenu = 0xA5;
    private const ushort VirtualKeyMenu = 0x12;
    private const ushort VirtualKeyDelete = 0x2E;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyRightWindows = 0x5C;
    private const ushort ScanCodeControl = 0x1D;
    private const ushort ScanCodeAlt = 0x38;
    private const ushort ScanCodeDelete = 0x53;
    private readonly object _sync = new();
    private readonly HashSet<ushort> _pressedVirtualKeys = [];
    private readonly HashSet<PhysicalKey> _pressedPhysicalKeys = [];

    public ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            switch (inputEvent.Kind)
            {
                case RemoteInputKind.MouseMove:
                    InjectMouseMove(inputEvent.NormalizedX, inputEvent.NormalizedY);
                    break;
                case RemoteInputKind.MouseButton:
                    InjectMouseButton(inputEvent.Button, inputEvent.IsDown);
                    break;
                case RemoteInputKind.MouseWheel:
                    InjectMouseWheel(inputEvent.WheelDelta);
                    break;
                case RemoteInputKind.Key:
                    InjectVirtualKey(inputEvent.VirtualKey, inputEvent.IsDown);
                    break;
                case RemoteInputKind.PhysicalKey:
                    InjectPhysicalKey(inputEvent.ScanCode, inputEvent.IsExtended, inputEvent.IsDown);
                    break;
                case RemoteInputKind.UnicodeText:
                    InjectUnicodeScalar(inputEvent.UnicodeScalar);
                    break;
                case RemoteInputKind.ReleaseAllKeys:
                    ReleaseAllKeys();
                    break;
                default:
                    throw new InvalidOperationException("Unsupported input event.");
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void InjectMouseMove(float normalizedX, float normalizedY)
    {
        int x = (int)Math.Round(Math.Clamp(normalizedX, 0, 1) * 65535d);
        int y = (int)Math.Round(Math.Clamp(normalizedY, 0, 1) * 65535d);
        INPUT input = INPUT.Mouse(x, y, 0, MouseMove | MouseAbsolute);
        SendOne(input);
    }

    private static void InjectMouseButton(RemoteMouseButton button, bool isDown)
    {
        uint flags = (button, isDown) switch
        {
            (RemoteMouseButton.Left, true) => MouseLeftDown,
            (RemoteMouseButton.Left, false) => MouseLeftUp,
            (RemoteMouseButton.Right, true) => MouseRightDown,
            (RemoteMouseButton.Right, false) => MouseRightUp,
            (RemoteMouseButton.Middle, true) => MouseMiddleDown,
            (RemoteMouseButton.Middle, false) => MouseMiddleUp,
            _ => throw new InvalidOperationException("Unsupported mouse button."),
        };
        SendOne(INPUT.Mouse(0, 0, 0, flags));
    }

    private static void InjectMouseWheel(int delta)
    {
        int bounded = Math.Clamp(delta, -1200, 1200);
        SendOne(INPUT.Mouse(0, 0, unchecked((uint)bounded), MouseWheel));
    }

    private void InjectVirtualKey(ushort virtualKey, bool isDown)
    {
        if (virtualKey == 0)
        {
            return;
        }

        bool controlHeld = _pressedVirtualKeys.Contains(VirtualKeyControl) ||
                           _pressedVirtualKeys.Contains(VirtualKeyLeftControl) ||
                           _pressedVirtualKeys.Contains(VirtualKeyRightControl);
        bool altHeld = _pressedVirtualKeys.Contains(VirtualKeyMenu) ||
                       _pressedVirtualKeys.Contains(VirtualKeyLeftMenu) ||
                       _pressedVirtualKeys.Contains(VirtualKeyRightMenu);
        if (isDown && virtualKey == VirtualKeyDelete && controlHeld && altHeld)
        {
            return;
        }

        if (isDown)
        {
            _pressedVirtualKeys.Add(virtualKey);
        }
        else
        {
            _pressedVirtualKeys.Remove(virtualKey);
        }

        uint flags = IsExtendedVirtualKey(virtualKey) ? KeyExtended : 0u;
        if (!isDown)
        {
            flags |= KeyUp;
        }

        INPUT input = INPUT.Keyboard(virtualKey, 0, flags);
        SendOne(input);
    }

    private void InjectPhysicalKey(ushort scanCode, bool isExtended, bool isDown)
    {
        PhysicalKey key = new(scanCode, isExtended);
        bool controlHeld = _pressedPhysicalKeys.Any(pressed => pressed.ScanCode == ScanCodeControl);
        bool altHeld = _pressedPhysicalKeys.Any(pressed => pressed.ScanCode == ScanCodeAlt);
        if (isDown && isExtended && scanCode == ScanCodeDelete && controlHeld && altHeld)
        {
            return;
        }

        if (isDown)
        {
            _pressedPhysicalKeys.Add(key);
        }
        else
        {
            _pressedPhysicalKeys.Remove(key);
        }

        uint flags = KeyScanCode |
                     (isExtended ? KeyExtended : 0u) |
                     (isDown ? 0u : KeyUp);
        SendOne(INPUT.Keyboard(0, scanCode, flags));
    }

    private static void InjectUnicodeScalar(int unicodeScalar)
    {
        string text = char.ConvertFromUtf32(unicodeScalar);
        INPUT[] inputs = new INPUT[text.Length * 2];
        for (int index = 0; index < text.Length; index++)
        {
            ushort codeUnit = text[index];
            inputs[index * 2] = INPUT.Keyboard(0, codeUnit, KeyUnicode);
            inputs[(index * 2) + 1] = INPUT.Keyboard(0, codeUnit, KeyUnicode | KeyUp);
        }

        SendMany(inputs);
    }

    private void ReleaseAllKeys()
    {
        List<INPUT> releases = [];
        foreach (PhysicalKey key in _pressedPhysicalKeys)
        {
            uint flags = KeyScanCode | KeyUp | (key.IsExtended ? KeyExtended : 0u);
            releases.Add(INPUT.Keyboard(0, key.ScanCode, flags));
        }

        foreach (ushort virtualKey in _pressedVirtualKeys)
        {
            uint flags = KeyUp | (IsExtendedVirtualKey(virtualKey) ? KeyExtended : 0u);
            releases.Add(INPUT.Keyboard(virtualKey, 0, flags));
        }

        _pressedPhysicalKeys.Clear();
        _pressedVirtualKeys.Clear();
        if (releases.Count > 0)
        {
            SendMany([.. releases]);
        }
    }

    private static bool IsExtendedVirtualKey(ushort virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or
        0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or
        VirtualKeyRightControl or VirtualKeyRightMenu;

    private static void SendOne(INPUT input)
    {
        SendMany([input]);
    }

    private static void SendMany(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 拒絕輸入注入；可能受到 UIPI 或桌面狀態限制。");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;

        public static INPUT Mouse(int x, int y, uint mouseData, uint flags) => new()
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                MouseInput = new MOUSEINPUT
                {
                    X = x,
                    Y = y,
                    MouseData = mouseData,
                    Flags = flags,
                },
            },
        };

        public static INPUT Keyboard(ushort virtualKey, ushort scanCode, uint flags) => new()
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                KeyboardInput = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                },
            },
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT MouseInput;

        [FieldOffset(0)]
        public KEYBDINPUT KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private readonly record struct PhysicalKey(ushort ScanCode, bool IsExtended);
}
