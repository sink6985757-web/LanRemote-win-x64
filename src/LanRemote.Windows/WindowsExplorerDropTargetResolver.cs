using System.Runtime.InteropServices;
using System.Text;
using LanRemote.Core;

namespace LanRemote.Windows;

public sealed class WindowsExplorerDropTargetResolver : IRemoteDropTargetResolver
{
    public ValueTask<string?> ResolveAsync(
        float normalizedX,
        float normalizedY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!float.IsFinite(normalizedX) || !float.IsFinite(normalizedY) ||
            normalizedX is < 0 or > 1 || normalizedY is < 0 or > 1)
        {
            return ValueTask.FromResult<string?>(null);
        }

        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.PrimaryScreen
            ?? System.Windows.Forms.Screen.AllScreens.First();
        System.Drawing.Rectangle bounds = screen.Bounds;
        Point point = new(
            bounds.Left + (int)Math.Round(normalizedX * Math.Max(0, bounds.Width - 1)),
            bounds.Top + (int)Math.Round(normalizedY * Math.Max(0, bounds.Height - 1)));
        nint hitWindow = WindowFromPoint(point);
        if (hitWindow == 0)
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (IsDesktopWindow(hitWindow))
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return ValueTask.FromResult<string?>(FileTransferPolicy.ValidateExistingDirectory(desktop));
        }

        nint rootWindow = GetAncestor(hitWindow, 2);
        string rootClass = GetWindowClass(rootWindow);
        if (rootClass is not ("CabinetWClass" or "ExploreWClass"))
        {
            return ValueTask.FromResult<string?>(null);
        }

        string? explorerPath = TryGetExplorerPath(rootWindow);
        if (explorerPath is null)
        {
            return ValueTask.FromResult<string?>(null);
        }

        return ValueTask.FromResult<string?>(FileTransferPolicy.ValidateExistingDirectory(explorerPath));
    }

    private static bool IsDesktopWindow(nint window)
    {
        for (nint current = window; current != 0; current = GetParent(current))
        {
            string className = GetWindowClass(current);
            if (className is "Progman" or "WorkerW" or "SHELLDLL_DefView")
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetExplorerPath(nint expectedWindow)
    {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            return null;
        }

        object? shellObject = null;
        object? windowsObject = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null)
            {
                return null;
            }

            dynamic shell = shellObject;
            windowsObject = shell.Windows();
            dynamic windows = windowsObject;
            int count = windows.Count;
            for (int index = 0; index < count; index++)
            {
                object? windowObject = null;
                try
                {
                    windowObject = windows.Item(index);
                    if (windowObject is null)
                    {
                        continue;
                    }

                    dynamic explorer = windowObject;
                    nint hwnd = (nint)(long)explorer.HWND;
                    if (hwnd != expectedWindow)
                    {
                        continue;
                    }

                    string? path = explorer.Document?.Folder?.Self?.Path as string;
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
                catch (COMException)
                {
                }
                finally
                {
                    ReleaseComObject(windowObject);
                }
            }

            return null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(windowsObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string GetWindowClass(nint window)
    {
        StringBuilder className = new(256);
        return GetClassName(window, className, className.Capacity) > 0 ? className.ToString() : string.Empty;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(int X, int Y);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maximumCount);
}
