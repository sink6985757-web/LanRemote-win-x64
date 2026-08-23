using System.Runtime.CompilerServices;

namespace LanRemote.Tests;

public sealed class UiRegressionTests
{
    [Fact]
    public void MainWindow_UsesOneSessionSurfaceWithoutVisibleTabs()
    {
        string root = FindSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("<TabControl", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TabItem", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionTabs", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionDesktopSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileTransferSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"⇄ 檔案\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FileTransferMenuItem", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowRemoteDesktopSurface();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameAndFailureUi_DoNotDependOnSelectedTabs()
    {
        string root = FindSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("SelectedItem != RemoteControlTab", code, StringComparison.Ordinal);
        Assert.Contains("if (_controller?.IsConnected != true)", code, StringComparison.Ordinal);
        Assert.Contains("_frameBuffer.Offer(frame);", code, StringComparison.Ordinal);
        Assert.Contains("\"未連線成功\"", code, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"CheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"#F8FAFC\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteInput_UsesLowLevelCaptureAndReservesOnlyLocalSecureAttention()
    {
        string root = FindSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"RemoteInputBorder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusInputText\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewTextInput=\"RemoteDisplay_PreviewTextInput\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ActivateRemoteInput();", code, StringComparison.Ordinal);
        Assert.Contains("WindowsLowLevelKeyboardCapture", code, StringComparison.Ordinal);
        Assert.Contains("ReleaseRemoteKeysForLocalSecureAttention", code, StringComparison.Ordinal);
        Assert.Contains("Window_Deactivated", code, StringComparison.Ordinal);
        Assert.Contains("RemoteInputEvent.ReleaseAllKeys()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteShortcut.TryGetClipboardHotkey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_awaitingUnicodeText", code, StringComparison.Ordinal);
        Assert.Contains("else if (e.Key == Key.F11)", code, StringComparison.Ordinal);
        Assert.Contains("else if (e.Key == Key.Escape)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FileTransferSelection_UsesReadableHighContrastColors()
    {
        string root = FindSolutionRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "LanRemote.App", "FileTransferPanel.xaml"));

        Assert.Contains("SystemColors.HighlightBrushKey", xaml, StringComparison.Ordinal);
        Assert.Contains("SystemColors.HighlightTextBrushKey", xaml, StringComparison.Ordinal);
        Assert.Contains("SystemColors.InactiveSelectionHighlightBrushKey", xaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#155E75\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Color=\"#F8FAFC\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationIcon_IsTransparentMultiResolutionAndWiredIntoWpf()
    {
        string root = FindSolutionRoot();
        string projectDirectory = Path.Combine(root, "src", "LanRemote.App");
        string project = File.ReadAllText(Path.Combine(projectDirectory, "LanRemote.App.csproj"));
        string window = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.xaml"));
        string packageScript = File.ReadAllText(Path.Combine(root, "scripts", "Build-Package.ps1"));
        string pngPath = Path.Combine(projectDirectory, "Assets", "LanRemote.App.png");
        string iconPath = Path.Combine(projectDirectory, "Assets", "LanRemote.App.ico");

        Assert.Contains("<ApplicationIcon>Assets\\LanRemote.App.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Assets\\LanRemote.App.ico\" />", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"Assets/LanRemote.App.ico\"", window, StringComparison.Ordinal);
        Assert.Contains("LanRemote.App.png", packageScript, StringComparison.Ordinal);
        Assert.Contains("LanRemote.App.ico", packageScript, StringComparison.Ordinal);
        Assert.Contains("ICON-PROVENANCE.md", packageScript, StringComparison.Ordinal);
        Assert.True(File.Exists(pngPath));
        Assert.True(File.Exists(iconPath));

        byte[] png = File.ReadAllBytes(pngPath);
        Assert.True(png.Length > 26);
        Assert.Equal((byte)6, png[25]); // PNG IHDR color type 6 = RGBA.

        using FileStream stream = File.OpenRead(iconPath);
        using BinaryReader reader = new(stream);
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        ushort count = reader.ReadUInt16();
        Assert.Equal((ushort)9, count);

        HashSet<int> sizes = [];
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            int height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width == 0 ? 256 : width, height == 0 ? 256 : height);
            reader.BaseStream.Position += 14;
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], sizes.Order());
    }

    private static string FindSolutionRoot([CallerFilePath] string sourceFile = "")
    {
        DirectoryInfo? directory = new(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LanRemote.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 LanRemote.sln，無法執行 UI regression 檢查。");
    }
}
