using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LanRemote.Windows;

public readonly record struct PhysicalKeyDescriptor(ushort ScanCode, bool IsExtended);

[SupportedOSPlatform("windows10.0.19041.0")]
public static class WindowsKeyboardMapper
{
    private const uint MapVirtualKeyToScanCodeExtended = 4;

    public static bool TryMapVirtualKey(int virtualKey, out PhysicalKeyDescriptor descriptor)
    {
        descriptor = default;
        if (virtualKey is <= 0 or > ushort.MaxValue)
        {
            return false;
        }

        uint mapped = MapVirtualKeyEx(
            (uint)virtualKey,
            MapVirtualKeyToScanCodeExtended,
            GetKeyboardLayout(0));
        ushort scanCode = (ushort)(mapped & 0xFF);
        if (scanCode == 0)
        {
            return false;
        }

        uint prefix = (mapped >> 8) & 0xFF;
        descriptor = new PhysicalKeyDescriptor(
            scanCode,
            prefix is 0xE0 or 0xE1 || IsExtendedVirtualKey(virtualKey));
        return true;
    }

    private static bool IsExtendedVirtualKey(int virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or // Page Up, Page Down, End, Home
        0x25 or 0x26 or 0x27 or 0x28 or // Arrow cluster
        0x2C or 0x2D or 0x2E or         // Print Screen, Insert, Delete
        0x5B or 0x5C or                 // Windows keys
        0x6F or                         // Numpad divide
        0x90 or                         // Num Lock
        0xA3 or 0xA5;                   // Right Ctrl, Right Alt

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint keyboardLayout);
}
