using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
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
    private bool _sessionViewActive;
    private bool _chromePinned = true;
    private bool _chromeTemporarilyRevealed;
    private bool _syncingQualityUi;
    private QualityPreset _selectedQualityPreset = QualityPreset.Balanced;
    private RemoteWindowMode _remoteWindowMode = RemoteWindowMode.Windowed;
    private RemoteWindowMode _modeBeforeFullscreen = RemoteWindowMode.Windowed;
    private Rect _windowedBounds;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SaveWindowedBounds();
        RefreshLocalAddresses();
        ApplyQualityUi(QualityProfiles.Get(_selectedQualityPreset));
        SetRoleUi();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded && _remoteWindowMode == RemoteWindowMode.Windowed)
        {
            SaveWindowedBounds();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded && _remoteWindowMode == RemoteWindowMode.Windowed)
        {
            SaveWindowedBounds();
        }
    }

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
        if (!_closing)
        {
            SetStatus("被控端已停止；現在可交換角色。");
        }

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
        controller.PairingCodeAvailable += code => RunOnUi(() =>
            PairingCodeText.Text = FormatPairingCode(code));
        controller.SessionReadyReceived += ready => RunOnUi(() => EnterSessionView(ready));
        controller.QualityProfileAppliedReceived += profile => RunOnUi(() =>
        {
            ApplyQualityUi(profile);
            SessionInfoText.Text = $"{profile.DisplayName}｜{profile.MaximumWidth}×{profile.MaximumHeight}｜" +
                                   $"{profile.FramesPerSecond} fps";
        });
        controller.VideoFrameReceived += DisplayFrame;
        controller.MetricsReceived += metrics => RunOnUi(() =>
        {
            long age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastFrameTimestamp);
            MetricsText.Text = $"{metrics.SentFrames} frames｜{metrics.LastFrameBytes / 1024d:F0} KiB｜" +
                               $"畫面年齡約 {age} ms";
        });

        _controller = controller;
        SetRoleUi();
        SetStatus($"正在連線至 {endpoint}…");
        try
        {
            await controller.ConnectAsync(endpoint, _selectedQualityPreset);
            SetRoleUi();
        }
        catch (Exception exception)
        {
            _controller = null;
            await controller.DisposeAsync();
            SetStatus($"控制端連線失敗：{exception.Message}");
            LeaveSessionView();
            SetRoleUi();
        }
    }

    private void EnterSessionView(SessionReady ready)
    {
        _remoteWidth = ready.Width;
        _remoteHeight = ready.Height;
        _sessionViewActive = true;
        LauncherView.Visibility = Visibility.Collapsed;
        SessionView.Visibility = Visibility.Visible;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        ApplyQualityUi(ready.QualityProfile);
        SetChromePinned(true);
        ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
        SessionInfoText.Text = $"{ready.QualityProfile.DisplayName}｜{ready.Width}×{ready.Height}｜{ready.Codec}";
        SetStatus($"控制 session 已啟用；拖曳視窗即可自適應畫面，F11 切換全螢幕。");
        SetRoleUi();
        RemoteDisplay.Focus();
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e) =>
        await DisconnectControllerAsync();

    private async Task DisconnectControllerAsync()
    {
        RemoteController? controller = _controller;
        _controller = null;
        if (controller is not null)
        {
            await controller.DisposeAsync();
        }

        LeaveSessionView();
        PairingCodeText.Text = "—— —— ——";
        MetricsText.Text = "尚無畫面資料";
        if (!_closing)
        {
            SetStatus("控制端已斷線；已返回連線介面並保留位址與畫面模式。");
        }

        SetRoleUi();
    }

    private void LeaveSessionView()
    {
        if (_remoteWindowMode != RemoteWindowMode.Windowed)
        {
            ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
        }

        _sessionViewActive = false;
        SetChromePinned(true);
        RemoteDisplay.Source = null;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        SessionView.Visibility = Visibility.Collapsed;
        LauncherView.Visibility = Visibility.Visible;
        _remoteWidth = 0;
        _remoteHeight = 0;
        SessionInfoText.Text = "尚未連線";
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

    private async void SmoothQualityMenuItem_Click(object sender, RoutedEventArgs e) =>
        await SelectQualityPresetAsync(QualityPreset.Smooth);

    private async void BalancedQualityMenuItem_Click(object sender, RoutedEventArgs e) =>
        await SelectQualityPresetAsync(QualityPreset.Balanced);

    private async void QualityQualityMenuItem_Click(object sender, RoutedEventArgs e) =>
        await SelectQualityPresetAsync(QualityPreset.Quality);

    private async void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingQualityUi)
        {
            return;
        }

        QualityPreset preset = QualityComboBox.SelectedIndex switch
        {
            0 => QualityPreset.Smooth,
            1 => QualityPreset.Balanced,
            2 => QualityPreset.Quality,
            _ => _selectedQualityPreset,
        };
        await SelectQualityPresetAsync(preset);
    }

    private async Task SelectQualityPresetAsync(QualityPreset preset)
    {
        QualityProfile profile = QualityProfiles.Get(preset);
        _selectedQualityPreset = preset;
        ApplyQualityUi(profile);
        RemoteController? controller = _controller;
        if (controller?.IsConnected == true)
        {
            SetStatus($"正在切換為{profile.DisplayName}模式…");
            try
            {
                await controller.ChangeQualityProfileAsync(preset);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                SetStatus($"畫面模式切換失敗：{exception.Message}");
            }
        }
        else
        {
            SetStatus($"已選擇{profile.DisplayName}模式；下次連線時套用。");
        }
    }

    private void ApplyQualityUi(QualityProfile profile)
    {
        _selectedQualityPreset = profile.Preset;
        _syncingQualityUi = true;
        try
        {
            SmoothQualityMenuItem.IsChecked = profile.Preset == QualityPreset.Smooth;
            BalancedQualityMenuItem.IsChecked = profile.Preset == QualityPreset.Balanced;
            QualityQualityMenuItem.IsChecked = profile.Preset == QualityPreset.Quality;
            QualityComboBox.SelectedIndex = profile.Preset switch
            {
                QualityPreset.Smooth => 0,
                QualityPreset.Balanced => 1,
                QualityPreset.Quality => 2,
                _ => 1,
            };
            SelectedQualityText.Text = $"{profile.DisplayName} — " +
                                       $"{profile.MaximumWidth}×{profile.MaximumHeight} / " +
                                       $"{profile.FramesPerSecond} fps";
        }
        finally
        {
            _syncingQualityUi = false;
        }
    }

    private void WindowedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyRemoteWindowMode(RemoteWindowMode.Windowed);

    private void MaximizedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyRemoteWindowMode(RemoteWindowMode.Maximized);

    private void FullscreenMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyRemoteWindowMode(RemoteWindowMode.Fullscreen);

    private void ApplyRemoteWindowMode(RemoteWindowMode mode)
    {
        if (!_sessionViewActive && mode != RemoteWindowMode.Windowed)
        {
            return;
        }

        if (_remoteWindowMode == RemoteWindowMode.Windowed && mode != RemoteWindowMode.Windowed)
        {
            SaveWindowedBounds();
        }

        if (mode == RemoteWindowMode.Fullscreen && _remoteWindowMode != RemoteWindowMode.Fullscreen)
        {
            _modeBeforeFullscreen = _remoteWindowMode;
        }

        if (_remoteWindowMode == RemoteWindowMode.Fullscreen && mode != RemoteWindowMode.Fullscreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }

        switch (mode)
        {
            case RemoteWindowMode.Windowed:
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                RestoreWindowedBounds();
                break;
            case RemoteWindowMode.Maximized:
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Maximized;
                break;
            case RemoteWindowMode.Fullscreen:
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                break;
            default:
                throw new InvalidOperationException("Unknown remote window mode.");
        }

        _remoteWindowMode = mode;
        UpdateWindowModeUi();
    }

    private void SaveWindowedBounds()
    {
        if (WindowState != WindowState.Normal || WindowStyle == WindowStyle.None ||
            ActualWidth < MinWidth || ActualHeight < MinHeight)
        {
            return;
        }

        _windowedBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
    }

    private void RestoreWindowedBounds()
    {
        if (_windowedBounds.Width < MinWidth || _windowedBounds.Height < MinHeight)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        Width = Math.Min(_windowedBounds.Width, workArea.Width);
        Height = Math.Min(_windowedBounds.Height, workArea.Height);
        Left = Math.Clamp(_windowedBounds.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(_windowedBounds.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void UpdateWindowModeUi()
    {
        WindowedMenuItem.IsChecked = _remoteWindowMode == RemoteWindowMode.Windowed;
        MaximizedMenuItem.IsChecked = _remoteWindowMode == RemoteWindowMode.Maximized;
        FullscreenMenuItem.IsChecked = _remoteWindowMode == RemoteWindowMode.Fullscreen;
    }

    private void ShowChromeMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetChromePinned(ShowChromeMenuItem.IsChecked);

    private void HideChromeButton_Click(object sender, RoutedEventArgs e) => SetChromePinned(false);

    private void SetChromePinned(bool pinned)
    {
        if (!_sessionViewActive)
        {
            pinned = true;
        }

        _chromePinned = pinned;
        _chromeTemporarilyRevealed = false;
        ShowChromeMenuItem.IsChecked = pinned;
        ChromePanel.Visibility = pinned || !_sessionViewActive ? Visibility.Visible : Visibility.Collapsed;
        TopRevealStrip.Visibility = !pinned && _sessionViewActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TopRevealStrip_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_sessionViewActive || _chromePinned)
        {
            return;
        }

        _chromeTemporarilyRevealed = true;
        ChromePanel.Visibility = Visibility.Visible;
        TopRevealStrip.Visibility = Visibility.Collapsed;
    }

    private void ChromePanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_sessionViewActive && !_chromePinned && _chromeTemporarilyRevealed)
        {
            _chromeTemporarilyRevealed = false;
            ChromePanel.Visibility = Visibility.Collapsed;
            TopRevealStrip.Visibility = Visibility.Visible;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_sessionViewActive)
        {
            return;
        }

        if (e.Key == Key.F11)
        {
            ApplyRemoteWindowMode(
                _remoteWindowMode == RemoteWindowMode.Fullscreen
                    ? _modeBeforeFullscreen
                    : RemoteWindowMode.Fullscreen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _remoteWindowMode == RemoteWindowMode.Fullscreen)
        {
            ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
            e.Handled = true;
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
        return AspectFitMapper.TryNormalizePoint(
            _remoteWidth,
            _remoteHeight,
            RemoteDisplay.ActualWidth,
            RemoteDisplay.ActualHeight,
            position.X,
            position.Y,
            out normalizedX,
            out normalizedY);
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
        bool session = _sessionViewActive && controlling;
        StartHostButton.IsEnabled = !hosting && !controlling;
        StopHostButton.IsEnabled = hosting;
        ConnectButton.IsEnabled = !hosting && !controlling;
        DisconnectButton.IsEnabled = controlling;
        HostPortTextBox.IsEnabled = !hosting && !controlling;
        RemoteEndpointTextBox.IsEnabled = !hosting && !controlling;
        StartHostMenuItem.IsEnabled = !hosting && !controlling;
        StopHostMenuItem.IsEnabled = hosting;
        ConnectMenuItem.IsEnabled = !hosting && !controlling;
        DisconnectMenuItem.IsEnabled = controlling;
        ToolbarDisconnectButton.IsEnabled = controlling;
        WindowedMenuItem.IsEnabled = session;
        MaximizedMenuItem.IsEnabled = session;
        FullscreenMenuItem.IsEnabled = session;
        ShowChromeMenuItem.IsEnabled = session;
        WindowedToolButton.IsEnabled = session;
        MaximizedToolButton.IsEnabled = session;
        FullscreenToolButton.IsEnabled = session;
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

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

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

    private enum RemoteWindowMode
    {
        Windowed,
        Maximized,
        Fullscreen,
    }
}
