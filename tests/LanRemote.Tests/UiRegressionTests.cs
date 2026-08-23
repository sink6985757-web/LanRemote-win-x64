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
