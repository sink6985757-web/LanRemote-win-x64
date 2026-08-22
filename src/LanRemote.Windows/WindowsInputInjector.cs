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
    private const uint KeyUp = 0x0002;
    private const ushort VirtualKeyLeftControl = 0xA2;
    private const ushort VirtualKeyRightControl = 0xA3;
    private const ushort VirtualKeyLeftMenu = 0xA4;
    private const ushort VirtualKeyRightMenu = 0xA5;
    private const ushort VirtualKeyDelete = 0x2E;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const ushort VirtualKeyRightWindows = 0x5C;
    private readonly object _sync = new();
    private readonly HashSet<ushort> _pressedKeys = [];

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
                    InjectKey(inputEvent.VirtualKey, inputEvent.IsDown);
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

    private void InjectKey(ushort virtualKey, bool isDown)
    {
        if (virtualKey is 0 or VirtualKeyLeftWindows or VirtualKeyRightWindows)
        {
            return;
        }

        bool controlHeld = _pressedKeys.Contains(VirtualKeyLeftControl) ||
                           _pressedKeys.Contains(VirtualKeyRightControl);
        bool altHeld = _pressedKeys.Contains(VirtualKeyLeftMenu) ||
                       _pressedKeys.Contains(VirtualKeyRightMenu);
        if (isDown && virtualKey == VirtualKeyDelete && controlHeld && altHeld)
        {
            return;
        }

        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }

        INPUT input = INPUT.Keyboard(virtualKey, isDown ? 0u : KeyUp);
        SendOne(input);
    }

    private static void SendOne(INPUT input)
    {
        INPUT[] inputs = [input];
        uint sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 1)
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

        public static INPUT Keyboard(ushort virtualKey, uint flags) => new()
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                KeyboardInput = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
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
}
