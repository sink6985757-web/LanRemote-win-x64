using System.Text.Json;
using LanRemote.Protocol;

namespace LanRemote.Core;

[Flags]
public enum ShortcutModifiers : byte
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
}

public sealed record RemoteShortcut(string Name, ushort VirtualKey, ShortcutModifiers Modifiers)
{
    public const ushort VirtualKeyDelete = 0x2E;
    public const ushort VirtualKeyEscape = 0x1B;
    public const ushort VirtualKeyTab = 0x09;
    public const ushort VirtualKeySpace = 0x20;
    public const ushort VirtualKeyF4 = 0x73;
    public const ushort VirtualKeyLeftWindows = 0x5B;
    public const ushort VirtualKeyRightWindows = 0x5C;

    public static RemoteShortcut ControlAltZero { get; } =
        new("Ctrl+Alt+0", 0x30, ShortcutModifiers.Control | ShortcutModifiers.Alt);

    public static RemoteShortcut ControlCopy { get; } =
        new("複製", 0x43, ShortcutModifiers.Control);

    public static RemoteShortcut ControlCut { get; } =
        new("剪下", 0x58, ShortcutModifiers.Control);

    public static RemoteShortcut ControlPaste { get; } =
        new("貼上", 0x56, ShortcutModifiers.Control);

    public static bool TryGetClipboardHotkey(ushort virtualKey, out RemoteShortcut? shortcut)
    {
        shortcut = virtualKey switch
        {
            0x43 => ControlCopy,
            0x58 => ControlCut,
            0x56 => ControlPaste,
            _ => null,
        };
        return shortcut is not null;
    }

    public IReadOnlyList<RemoteInputEvent> ToInputEvents()
    {
        List<RemoteInputEvent> events = [];
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Control), 0x11, true);
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Alt), 0x12, true);
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Shift), 0x10, true);
        events.Add(new RemoteInputEvent(RemoteInputKind.Key, IsDown: true, VirtualKey: VirtualKey));
        events.Add(new RemoteInputEvent(RemoteInputKind.Key, IsDown: false, VirtualKey: VirtualKey));
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Shift), 0x10, false);
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Alt), 0x12, false);
        AddModifier(events, Modifiers.HasFlag(ShortcutModifiers.Control), 0x11, false);
        return events;
    }

    public string GestureText =>
        $"{(Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl+" : string.Empty)}" +
        $"{(Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt+" : string.Empty)}" +
        $"{(Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift+" : string.Empty)}" +
        VirtualKeyName(VirtualKey);

    public static bool TryValidate(RemoteShortcut? shortcut, out string error)
    {
        if (shortcut is null)
        {
            error = "快捷鍵資料不可為空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(shortcut.Name) || shortcut.Name.Trim().Length > 40)
        {
            error = "名稱必須為 1 到 40 個字元。";
            return false;
        }

        if (shortcut.VirtualKey is 0 or 0x10 or 0x11 or 0x12 or
            VirtualKeyLeftWindows or VirtualKeyRightWindows)
        {
            error = "請指定一個非修飾鍵的主要按鍵，且不可使用 Windows 鍵。";
            return false;
        }

        ShortcutModifiers allowed = ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift;
        if ((shortcut.Modifiers & ~allowed) != 0)
        {
            error = "包含不支援的修飾鍵。";
            return false;
        }

        bool control = shortcut.Modifiers.HasFlag(ShortcutModifiers.Control);
        bool alt = shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt);
        bool shift = shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift);
        if ((control && alt && shortcut.VirtualKey == VirtualKeyDelete) ||
            (alt && shortcut.VirtualKey == VirtualKeyF4) ||
            (alt && shortcut.VirtualKey is VirtualKeyTab or VirtualKeyEscape or VirtualKeySpace) ||
            (control && shortcut.VirtualKey == VirtualKeyEscape) ||
            (control && shift && shortcut.VirtualKey == VirtualKeyEscape))
        {
            error = "這是 Windows 保留快捷鍵，不能加入自訂清單。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void AddModifier(
        ICollection<RemoteInputEvent> events,
        bool enabled,
        ushort virtualKey,
        bool isDown)
    {
        if (enabled)
        {
            events.Add(new RemoteInputEvent(RemoteInputKind.Key, IsDown: isDown, VirtualKey: virtualKey));
        }
    }

    private static string VirtualKeyName(ushort virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 || virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        return $"VK 0x{virtualKey:X2}";
    }
}

public sealed class RemoteShortcutStore
{
    public const int MaximumShortcutCount = 20;
    private readonly string _path;

    public RemoteShortcutStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public IReadOnlyList<RemoteShortcut> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(_path);
            RemoteShortcut[] shortcuts = JsonSerializer.Deserialize<RemoteShortcut[]>(json) ?? [];
            return shortcuts
                .Where(shortcut => RemoteShortcut.TryValidate(shortcut, out _))
                .Take(MaximumShortcutCount)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<RemoteShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        RemoteShortcut[] values = shortcuts.Take(MaximumShortcutCount + 1).ToArray();
        if (values.Length > MaximumShortcutCount)
        {
            throw new InvalidOperationException($"自訂快捷鍵最多 {MaximumShortcutCount} 組。");
        }

        foreach (RemoteShortcut shortcut in values)
        {
            if (!RemoteShortcut.TryValidate(shortcut, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(values, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        File.Move(temporary, _path, overwrite: true);
    }
}
