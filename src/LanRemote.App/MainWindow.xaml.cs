using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LanRemote.Core;
using LanRemote.Protocol;
using LanRemote.Windows;

namespace LanRemote.App;

public partial class MainWindow : Window
{
    private readonly Stopwatch _mouseMoveThrottle = Stopwatch.StartNew();
    private RemoteHost? _host;
    private RemoteController? _controller;
    private int _remoteWidth;
    private int _remoteHeight;
    private long _lastFrameTimestamp;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => RefreshLocalAddresses();

    private async void StartHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_host is not null || _controller is not null)
        {
            SetStatus("請先停止目前角色，再交換成被控端。");
            return;
        }

        if (!int.TryParse(HostPortTextBox.Text, out int port) || port is < 1024 or > 65535)
        {
            SetStatus("連接埠必須介於 1024 與 65535。");
            return;
        }

        RemoteHost host = new(new GdiJpegScreenFrameSource(), new WindowsInputInjector());
        host.StatusChanged += message => RunOnUi(() => SetStatus(message));
        host.MetricsChanged += metrics => RunOnUi(() =>
            MetricsText.Text = $"送出 {metrics.SentFrames} frames｜最後 {metrics.LastFrameBytes / 1024d:F0} KiB");
        host.PairingApprovalHandler = async (request, cancellationToken) =>
        {
            return await Dispatcher.InvokeAsync(
                () => ApprovePairing(request),
                System.Windows.Threading.DispatcherPriority.Send,
                cancellationToken);
        };

        try
        {
            await host.StartAsync(port);
            _host = host;
            RefreshLocalAddresses();
            SetRoleUi();
        }
        catch (Exception exception)
        {
            await host.DisposeAsync();
            SetStatus($"被控端啟動失敗：{exception.Message}");
        }
    }

    private bool ApprovePairing(PairingRequest request)
    {
        PairingCodeText.Text = FormatPairingCode(request.PairingCode);
        Activate();
        string message =
            $"裝置：{request.DeviceName}\n" +
            $"來源：{request.RemoteEndpoint.Address}\n" +
            $"系統：{request.OperatingSystem}\n\n" +
            $"配對碼：{FormatPairingCode(request.PairingCode)}\n\n" +
            "確認控制端顯示相同配對碼後，才按『是』。";
        MessageBoxResult result = MessageBox.Show(
            this,
            message,
            "允許本次遠端控制？",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private async void StopHostButton_Click(object sender, RoutedEventArgs e) => await StopHostAsync();

    private async Task StopHostAsync()
    {
        RemoteHost? host = _host;
        _host = null;
        if (host is not null)
        {
            await host.DisposeAsync();
        }

        PairingCodeText.Text = "—— —— ——";
        SetStatus("被控端已停止；現在可交換角色。");
        SetRoleUi();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_host is not null || _controller is not null)
        {
            SetStatus("請先停止目前角色，再交換成控制端。");
            return;
        }

        IPEndPoint endpoint;
        try
        {
            endpoint = LanEndpointParser.Parse(RemoteEndpointTextBox.Text);
        }
        catch (FormatException exception)
        {
            SetStatus(exception.Message);
            return;
        }

        RemoteController controller = new();
        controller.StatusChanged += message => RunOnUi(() => SetStatus(message));
        controller.PairingCodeAvailable += code => RunOnUi(() => PairingCodeText.Text = FormatPairingCode(code));
        controller.SessionReadyReceived += ready => RunOnUi(() =>
        {
            _remoteWidth = ready.Width;
            _remoteHeight = ready.Height;
            SetStatus($"控制 session 已啟用：{ready.Width}×{ready.Height}，{ready.Codec} 相容模式。");
            SetRoleUi();
        });
        controller.VideoFrameReceived += DisplayFrame;
        controller.MetricsReceived += metrics => RunOnUi(() =>
        {
            long age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastFrameTimestamp);
            MetricsText.Text = $"{metrics.SentFrames} frames｜{metrics.LastFrameBytes / 1024d:F0} KiB｜畫面年齡約 {age} ms";
        });

        _controller = controller;
        SetRoleUi();
        SetStatus($"正在連線至 {endpoint}…");
        try
        {
            await controller.ConnectAsync(endpoint);
            SetRoleUi();
        }
        catch (Exception exception)
        {
            _controller = null;
            await controller.DisposeAsync();
            SetStatus($"控制端連線失敗：{exception.Message}");
            SetRoleUi();
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e) => await DisconnectControllerAsync();

    private async Task DisconnectControllerAsync()
    {
        RemoteController? controller = _controller;
        _controller = null;
        if (controller is not null)
        {
            await controller.DisposeAsync();
        }

        RemoteDisplay.Source = null;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        PairingCodeText.Text = "—— —— ——";
        MetricsText.Text = "尚無畫面資料";
        _remoteWidth = 0;
        _remoteHeight = 0;
        SetStatus("控制端已斷線；現在可交換角色。");
        SetRoleUi();
    }

    private void DisplayFrame(VideoFramePayload frame)
    {
        if (frame.Codec != VideoCodec.Jpeg)
        {
            RunOnUi(() => SetStatus($"目前 UI 尚未支援解碼 {frame.Codec}。"));
            return;
        }

        try
        {
            BitmapImage bitmap = new();
            using MemoryStream stream = new(frame.Data, writable: false);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _lastFrameTimestamp = frame.CapturedAtUnixMilliseconds;
            RunOnUi(() =>
            {
                _remoteWidth = frame.Width;
                _remoteHeight = frame.Height;
                RemoteDisplay.Source = bitmap;
                RemoteDisplayPlaceholder.Visibility = Visibility.Collapsed;
                RemoteDisplay.Visibility = Visibility.Visible;
            });
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException)
        {
            RunOnUi(() => SetStatus($"遠端畫面解碼失敗：{exception.Message}"));
        }
    }

    private void RemoteDisplay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_controller?.IsConnected != true || _mouseMoveThrottle.ElapsedMilliseconds < 20)
        {
            return;
        }

        _mouseMoveThrottle.Restart();
        if (TryNormalizePosition(e.GetPosition(RemoteDisplay), out float x, out float y))
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.MouseMove, x, y));
        }
    }

    private void RemoteDisplay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_controller?.IsConnected != true || !TryMapButton(e.ChangedButton, out RemoteMouseButton button))
        {
            return;
        }

        RemoteDisplay.Focus();
        Mouse.Capture(RemoteDisplay);
        if (TryNormalizePosition(e.GetPosition(RemoteDisplay), out float x, out float y))
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.MouseMove, x, y));
        }

        SendInput(new RemoteInputEvent(RemoteInputKind.MouseButton, Button: button, IsDown: true));
        e.Handled = true;
    }

    private void RemoteDisplay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_controller?.IsConnected != true || !TryMapButton(e.ChangedButton, out RemoteMouseButton button))
        {
            return;
        }

        SendInput(new RemoteInputEvent(RemoteInputKind.MouseButton, Button: button, IsDown: false));
        Mouse.Capture(null);
        e.Handled = true;
    }

    private void RemoteDisplay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_controller?.IsConnected == true)
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.MouseWheel, WheelDelta: e.Delta));
            e.Handled = true;
        }
    }

    private void RemoteDisplay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_controller?.IsConnected != true || e.IsRepeat)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is > 0 and <= ushort.MaxValue)
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.Key, IsDown: true, VirtualKey: (ushort)virtualKey));
            e.Handled = true;
        }
    }

    private void RemoteDisplay_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_controller?.IsConnected != true)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is > 0 and <= ushort.MaxValue)
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.Key, IsDown: false, VirtualKey: (ushort)virtualKey));
            e.Handled = true;
        }
    }

    private void SendInput(RemoteInputEvent inputEvent) => _ = SendInputSafelyAsync(inputEvent);

    private async Task SendInputSafelyAsync(RemoteInputEvent inputEvent)
    {
        RemoteController? controller = _controller;
        if (controller is null)
        {
            return;
        }

        try
        {
            await controller.SendInputAsync(inputEvent);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            RunOnUi(() => SetStatus($"輸入傳送中斷：{exception.Message}"));
        }
    }

    private bool TryNormalizePosition(Point position, out float normalizedX, out float normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        if (_remoteWidth <= 0 || _remoteHeight <= 0 || RemoteDisplay.ActualWidth <= 0 || RemoteDisplay.ActualHeight <= 0)
        {
            return false;
        }

        double imageRatio = (double)_remoteWidth / _remoteHeight;
        double controlRatio = RemoteDisplay.ActualWidth / RemoteDisplay.ActualHeight;
        double renderedWidth;
        double renderedHeight;
        double offsetX;
        double offsetY;
        if (controlRatio > imageRatio)
        {
            renderedHeight = RemoteDisplay.ActualHeight;
            renderedWidth = renderedHeight * imageRatio;
            offsetX = (RemoteDisplay.ActualWidth - renderedWidth) / 2d;
            offsetY = 0;
        }
        else
        {
            renderedWidth = RemoteDisplay.ActualWidth;
            renderedHeight = renderedWidth / imageRatio;
            offsetX = 0;
            offsetY = (RemoteDisplay.ActualHeight - renderedHeight) / 2d;
        }

        double x = (position.X - offsetX) / renderedWidth;
        double y = (position.Y - offsetY) / renderedHeight;
        if (x is < 0 or > 1 || y is < 0 or > 1)
        {
            return false;
        }

        normalizedX = (float)x;
        normalizedY = (float)y;
        return true;
    }

    private static bool TryMapButton(MouseButton input, out RemoteMouseButton output)
    {
        output = input switch
        {
            MouseButton.Left => RemoteMouseButton.Left,
            MouseButton.Right => RemoteMouseButton.Right,
            MouseButton.Middle => RemoteMouseButton.Middle,
            _ => 0,
        };
        return output != 0;
    }

    private void RefreshLocalAddresses()
    {
        int port = int.TryParse(HostPortTextBox.Text, out int parsedPort) ? parsedPort : 45873;
        IReadOnlyList<IPAddress> addresses = LanAddressPolicy.GetLocalPrivateAddresses();
        LocalAddressesText.Text = addresses.Count == 0
            ? "找不到私人 IPv4；請確認兩臺都連到同一個私人網路。"
            : string.Join("   ｜   ", addresses.Select(address => $"{address}:{port}"));
    }

    private void SetRoleUi()
    {
        bool hosting = _host is not null;
        bool controlling = _controller is not null;
        StartHostButton.IsEnabled = !hosting && !controlling;
        StopHostButton.IsEnabled = hosting;
        ConnectButton.IsEnabled = !hosting && !controlling;
        DisconnectButton.IsEnabled = controlling;
        HostPortTextBox.IsEnabled = !hosting && !controlling;
        RemoteEndpointTextBox.IsEnabled = !hosting && !controlling;
        ModeBadge.Text = hosting ? "被控端模式" : controlling ? "控制端模式" : "尚未連線";
    }

    private static string FormatPairingCode(string code) =>
        code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;

    private void SetStatus(string message) => StatusText.Text = message;

    private void RunOnUi(Action action)
    {
        if (!_closing)
        {
            _ = Dispatcher.InvokeAsync(action);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;
        IsEnabled = false;
        try
        {
            await DisconnectControllerAsync();
            await StopHostAsync();
        }
        finally
        {
            e.Cancel = false;
            Close();
        }
    }
}
