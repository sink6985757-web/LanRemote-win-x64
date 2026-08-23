using System.Windows;
using System.Windows.Input;
using LanRemote.Core;

namespace LanRemote.App;

public partial class ShortcutEditorWindow : Window
{
    private ushort _virtualKey;
    private ShortcutModifiers _modifiers;
    private bool _capturing;

    public ShortcutEditorWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public RemoteShortcut? Shortcut { get; private set; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift)
        {
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is <= 0 or > ushort.MaxValue)
        {
            return;
        }

        _virtualKey = (ushort)virtualKey;
        _modifiers = ShortcutModifiers.None;
        ModifierKeys keyboardModifiers = Keyboard.Modifiers;
        if (keyboardModifiers.HasFlag(ModifierKeys.Control))
        {
            _modifiers |= ShortcutModifiers.Control;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Alt))
        {
            _modifiers |= ShortcutModifiers.Alt;
        }

        if (keyboardModifiers.HasFlag(ModifierKeys.Shift))
        {
            _modifiers |= ShortcutModifiers.Shift;
        }

        RemoteShortcut preview = new("preview", _virtualKey, _modifiers);
        GestureText.Text = preview.GestureText;
        _capturing = false;
        e.Handled = true;
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        GestureText.Text = "請按組合鍵…";
        CaptureButton.Focus();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        RemoteShortcut candidate = new(NameTextBox.Text.Trim(), _virtualKey, _modifiers);
        if (!RemoteShortcut.TryValidate(candidate, out string error))
        {
            MessageBox.Show(this, error, "無法新增快捷鍵", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Shortcut = candidate;
        DialogResult = true;
    }
}
