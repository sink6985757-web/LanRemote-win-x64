using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanRemote.Core;
using LanRemote.Protocol;
using LanRemote.Windows;
using Microsoft.Win32;

namespace LanRemote.App;

public partial class MainWindow : Window
{
    private readonly Stopwatch _mouseMoveThrottle = Stopwatch.StartNew();
    private readonly Stopwatch _presentationRateTimer = Stopwatch.StartNew();
    private readonly RemoteShortcutStore _shortcutStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LanRemote",
        "shortcuts.json"));
    private readonly List<RemoteShortcut> _customShortcuts = [];
    private readonly LatestValueBuffer<VideoFramePayload> _frameBuffer = new();
    private readonly DispatcherTimer _statusToastTimer = new()
    {
        Interval = TimeSpan.FromSeconds(4),
    };
    private RemoteHost? _host;
    private RemoteController? _controller;
    private FileTransferWindow? _fileTransferWindow;
    private CancellationTokenSource? _directTransferCancellation;
    private Guid? _activeTransferId;
    private FileTransferDirection _activeTransferDirection;
    private MetricsPayload? _lastMetrics;
    private int _framePresentationScheduled;
    private long _presentedFramesInInterval;
    private double _currentPresentationFps;
    private int _remoteWidth;
    private int _remoteHeight;
    private long _lastFrameTimestamp;
    private bool _closing;
    private volatile bool _sessionViewActive;
    private bool _chromePinned = true;
    private bool _chromeTemporarilyRevealed;
    private bool _syncingQualityUi;
    private bool _syncingScaleUi;
    private QualityPreset _selectedQualityPreset = QualityPreset.Balanced;
    private RemoteScaleMode _scaleMode = RemoteScaleMode.Stretch;
    private RemoteWindowMode _remoteWindowMode = RemoteWindowMode.Windowed;
    private RemoteWindowMode _modeBeforeFullscreen = RemoteWindowMode.Windowed;
    private Rect _windowedBounds;
    private StatusDisplayMode _statusDisplayMode = StatusDisplayMode.Simple;
    private string _lastStatusMessage = "請選擇這臺電腦本次要扮演的角色。";
    private string? _resolvedDropTarget;
    private Point _lastDropResolvePoint;
    private DateTimeOffset _lastDropResolveAt;
    private bool _dropResolveInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _statusToastTimer.Tick += (_, _) =>
        {
            _statusToastTimer.Stop();
            TransientStatusBorder.Visibility = Visibility.Collapsed;
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SaveWindowedBounds();
        RefreshLocalAddresses();
        ApplyQualityUi(QualityProfiles.Get(_selectedQualityPreset));
        _customShortcuts.AddRange(_shortcutStore.Load());
        RebuildShortcutMenus();
        ApplyScaleMode(RemoteScaleMode.Stretch);
        ApplyStatusDisplayMode(StatusDisplayMode.Simple);
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

        RemoteHost host = new(
            new GdiJpegScreenFrameSource(),
            new WindowsInputInjector(),
            new NamedPipeSecureAttentionProvider(),
            new WindowsExplorerDropTargetResolver(),
            new WindowsClipboardFileProvider());
        host.StatusChanged += message => RunOnUi(() => SetStatus(message));
        host.MetricsChanged += metrics => RunOnUi(() =>
        {
            _lastMetrics = metrics;
            UpdateStatusDisplay();
        });
        host.FileTransferProgressChanged += progress => RunOnUi(() => UpdateTransferProgress(progress));
        host.FileTransferCompleted += result => RunOnUi(() => CompleteTransferStatus(result));
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

    private PairingApproval ApprovePairing(PairingRequest request)
    {
        PairingCodeText.Text = FormatPairingCode(request.PairingCode);
        Activate();
        PairingApprovalWindow dialog = new(request)
        {
            Owner = this,
        };
        _ = dialog.ShowDialog();
        return dialog.Approval;
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
        controller.SecureAttentionResultReceived += result => RunOnUi(() =>
        {
            SetStatus(result.Message);
            if (!result.Succeeded)
            {
                MessageBox.Show(
                    this,
                    result.Message + "\n\n程式不會自動安裝服務或修改 Windows 安全性原則。",
                    "Ctrl+Alt+Delete 尚未就緒",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        });
        controller.FileTransferProgressChanged += progress => RunOnUi(() => UpdateTransferProgress(progress));
        controller.FileTransferCompleted += result => RunOnUi(() => CompleteTransferStatus(result));
        controller.VideoFrameReceived += EnqueueFrame;
        controller.MetricsReceived += metrics => RunOnUi(() =>
        {
            _lastMetrics = metrics;
            UpdateStatusDisplay();
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
        _frameBuffer.Reset();
        _presentedFramesInInterval = 0;
        _currentPresentationFps = 0;
        _presentationRateTimer.Restart();
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
        UpdateStatusDisplay();
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
        _fileTransferWindow?.Close();
        _fileTransferWindow = null;
        PairingCodeText.Text = "—— —— ——";
        _lastMetrics = null;
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
        _frameBuffer.Clear();
        Interlocked.Exchange(ref _framePresentationScheduled, 0);
        ControllerCursorLayer.Visibility = Visibility.Collapsed;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        SessionView.Visibility = Visibility.Collapsed;
        LauncherView.Visibility = Visibility.Visible;
        _remoteWidth = 0;
        _remoteHeight = 0;
        SessionInfoText.Text = "尚未連線";
        StatusTransferText.Visibility = Visibility.Collapsed;
        CancelActiveTransferToolButton.Visibility = Visibility.Collapsed;
        _resolvedDropTarget = null;
        DropTargetOverlay.Visibility = Visibility.Collapsed;
        UpdateStatusDisplay();
    }

    private void EnqueueFrame(VideoFramePayload frame)
    {
        if (!_sessionViewActive)
        {
            return;
        }

        _frameBuffer.Offer(frame);
        ScheduleFramePresentation();
    }

    private void ScheduleFramePresentation()
    {
        if (Interlocked.CompareExchange(ref _framePresentationScheduled, 1, 0) == 0)
        {
            RunOnUi(PresentLatestFrame);
        }
    }

    private void PresentLatestFrame()
    {
        VideoFramePayload? frame = _frameBuffer.Take();
        if (frame is null)
        {
            Interlocked.Exchange(ref _framePresentationScheduled, 0);
            return;
        }

        if (frame.Codec != VideoCodec.Jpeg)
        {
            SetStatus($"目前 UI 尚未支援解碼 {frame.Codec}。");
            CompleteFramePresentation();
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
            _remoteWidth = frame.Width;
            _remoteHeight = frame.Height;
            RemoteDisplay.Source = bitmap;
            RemoteDisplayPlaceholder.Visibility = Visibility.Collapsed;
            RemoteDisplay.Visibility = Visibility.Visible;
            _presentedFramesInInterval++;
            if (_presentationRateTimer.Elapsed >= TimeSpan.FromSeconds(1))
            {
                _currentPresentationFps = _presentedFramesInInterval / _presentationRateTimer.Elapsed.TotalSeconds;
                _presentedFramesInInterval = 0;
                _presentationRateTimer.Restart();
                UpdateStatusDisplay();
            }
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException)
        {
            SetStatus($"遠端畫面解碼失敗：{exception.Message}");
        }

        CompleteFramePresentation();
    }

    private void CompleteFramePresentation()
    {
        Interlocked.Exchange(ref _framePresentationScheduled, 0);
        if (_frameBuffer.HasValue)
        {
            ScheduleFramePresentation();
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

    private void FitScaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyScaleMode(RemoteScaleMode.Fit);

    private void StretchScaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyScaleMode(RemoteScaleMode.Stretch);

    private void CropScaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyScaleMode(RemoteScaleMode.Crop);

    private void ScaleModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingScaleUi)
        {
            return;
        }

        ApplyScaleMode(ScaleModeComboBox.SelectedIndex switch
        {
            0 => RemoteScaleMode.Fit,
            1 => RemoteScaleMode.Stretch,
            2 => RemoteScaleMode.Crop,
            _ => RemoteScaleMode.Stretch,
        });
    }

    private void ApplyScaleMode(RemoteScaleMode mode)
    {
        _scaleMode = mode;
        RemoteDisplay.Stretch = mode switch
        {
            RemoteScaleMode.Fit => Stretch.Uniform,
            RemoteScaleMode.Stretch => Stretch.Fill,
            RemoteScaleMode.Crop => Stretch.UniformToFill,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        FitScaleMenuItem.IsChecked = mode == RemoteScaleMode.Fit;
        StretchScaleMenuItem.IsChecked = mode == RemoteScaleMode.Stretch;
        CropScaleMenuItem.IsChecked = mode == RemoteScaleMode.Crop;
        _syncingScaleUi = true;
        ScaleModeComboBox.SelectedIndex = mode switch
        {
            RemoteScaleMode.Fit => 0,
            RemoteScaleMode.Stretch => 1,
            RemoteScaleMode.Crop => 2,
            _ => 1,
        };
        _syncingScaleUi = false;

        if (_sessionViewActive)
        {
            string displayName = mode switch
            {
                RemoteScaleMode.Fit => "符合視窗",
                RemoteScaleMode.Stretch => "拉伸滿版",
                RemoteScaleMode.Crop => "裁切滿版",
                _ => mode.ToString(),
            };
            SetStatus($"縮放模式已切換為{displayName}。");
        }
    }

    private void StatusOffMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyStatusDisplayMode(StatusDisplayMode.Off);

    private void StatusSimpleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyStatusDisplayMode(StatusDisplayMode.Simple);

    private void StatusDetailedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyStatusDisplayMode(StatusDisplayMode.Detailed);

    private void ApplyStatusDisplayMode(StatusDisplayMode mode)
    {
        _statusDisplayMode = mode;
        StatusOffMenuItem.IsChecked = mode == StatusDisplayMode.Off;
        StatusSimpleMenuItem.IsChecked = mode == StatusDisplayMode.Simple;
        StatusDetailedMenuItem.IsChecked = mode == StatusDisplayMode.Detailed;
        ToolbarStatusPanel.Visibility = mode == StatusDisplayMode.Off
            ? Visibility.Collapsed
            : Visibility.Visible;
        DetailedStatusPanel.Visibility = mode == StatusDisplayMode.Detailed
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        bool hosting = _host is not null;
        bool connected = _controller?.IsConnected == true;
        StatusConnectionText.Text = connected
            ? "● 已連線"
            : hosting ? "● 等候連線" : "● 尚未連線";
        StatusConnectionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            connected ? "#047857" : hosting ? "#B45309" : "#64748B"));
        StatusFpsText.Text = connected ? $"｜{_currentPresentationFps:F1} FPS" : string.Empty;

        QualityProfile profile = _controller?.CurrentQualityProfile ?? QualityProfiles.Get(_selectedQualityPreset);
        SessionInfoText.Text = connected
            ? $"｜目標 {profile.FramesPerSecond} FPS｜{_remoteWidth}×{_remoteHeight}"
            : hosting ? "｜被控端" : string.Empty;
        if (_lastMetrics is MetricsPayload metrics)
        {
            long age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastFrameTimestamp);
            MetricsText.Text = connected
                ? $"｜丟棄 {_frameBuffer.DroppedCount}｜{metrics.LastFrameBytes / 1024d:F0} KiB｜畫面 {age} ms｜{_lastStatusMessage}"
                : $"｜已送出 {metrics.SentFrames}｜{metrics.LastFrameBytes / 1024d:F0} KiB｜{_lastStatusMessage}";
        }
        else
        {
            MetricsText.Text = $"｜{_lastStatusMessage}";
        }
    }

    private void UpdateTransferProgress(FileTransferProgress progress)
    {
        _activeTransferId = progress.TransferId;
        _activeTransferDirection = progress.Direction;
        double percentage = progress.TotalBytes == 0
            ? 100
            : progress.TransferredBytes * 100d / progress.TotalBytes;
        StatusTransferText.Text =
            $"｜{(progress.Direction == FileTransferDirection.Upload ? "上傳" : "下載")} {percentage:F0}%";
        StatusTransferText.Visibility = Visibility.Visible;
        CancelActiveTransferToolButton.Visibility = _controller is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateStatusDisplay();
    }

    private void CompleteTransferStatus(FileTransferResult result)
    {
        _activeTransferId = null;
        CancelActiveTransferToolButton.Visibility = Visibility.Collapsed;
        StatusTransferText.Visibility = Visibility.Collapsed;
        SetStatus(result.Message);
    }

    private async void SasToolButton_Click(object sender, RoutedEventArgs e)
    {
        RemoteController? controller = _controller;
        if (controller?.IsConnected != true)
        {
            SetStatus("請先建立控制 session，再送出 Ctrl+Alt+Delete。");
            return;
        }

        try
        {
            SetStatus("正在請求被控端 SAS 服務送出 Ctrl+Alt+Delete…");
            _ = await controller.RequestSecureAttentionAsync();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            SetStatus($"Ctrl+Alt+Delete 傳送失敗：{exception.Message}");
        }
    }

    private async void CtrlAltZeroToolButton_Click(object sender, RoutedEventArgs e) =>
        await SendShortcutSafelyAsync(RemoteShortcut.ControlAltZero);

    private void AddCustomShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_customShortcuts.Count >= RemoteShortcutStore.MaximumShortcutCount)
        {
            SetStatus($"自訂快捷鍵最多 {RemoteShortcutStore.MaximumShortcutCount} 組。");
            return;
        }

        ShortcutEditorWindow editor = new()
        {
            Owner = this,
        };
        if (editor.ShowDialog() != true || editor.Shortcut is null)
        {
            return;
        }

        if (_customShortcuts.Any(shortcut =>
                shortcut.VirtualKey == editor.Shortcut.VirtualKey &&
                shortcut.Modifiers == editor.Shortcut.Modifiers))
        {
            SetStatus("相同的按鍵組合已存在。");
            return;
        }

        _customShortcuts.Add(editor.Shortcut);
        PersistShortcuts();
    }

    private async void CustomShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RemoteShortcut shortcut })
        {
            await SendShortcutSafelyAsync(shortcut);
        }
    }

    private void RemoveCustomShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: RemoteShortcut shortcut })
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            $"要移除「{shortcut.Name}」（{shortcut.GestureText}）嗎？",
            "移除自訂按鍵",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _customShortcuts.Remove(shortcut);
        PersistShortcuts();
    }

    private void PersistShortcuts()
    {
        try
        {
            _shortcutStore.Save(_customShortcuts);
            RebuildShortcutMenus();
            SetStatus("自訂按鍵已保存於這個 Windows 使用者的本機設定。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"自訂按鍵保存失敗：{exception.Message}");
        }
    }

    private void RebuildShortcutMenus()
    {
        CustomShortcutEntriesMenu.Items.Clear();
        RemoveCustomShortcutMenu.Items.Clear();
        if (_customShortcuts.Count == 0)
        {
            CustomShortcutEntriesMenu.Items.Add(new MenuItem
            {
                Header = "尚未新增",
                IsEnabled = false,
            });
            RemoveCustomShortcutMenu.Items.Add(new MenuItem
            {
                Header = "沒有可移除的項目",
                IsEnabled = false,
            });
            return;
        }

        foreach (RemoteShortcut shortcut in _customShortcuts)
        {
            MenuItem sendItem = new()
            {
                Header = $"{shortcut.Name}  ({shortcut.GestureText})",
                Tag = shortcut,
                IsEnabled = _controller?.IsConnected == true,
            };
            sendItem.Click += CustomShortcutMenuItem_Click;
            CustomShortcutEntriesMenu.Items.Add(sendItem);

            MenuItem removeItem = new()
            {
                Header = $"{shortcut.Name}  ({shortcut.GestureText})",
                Tag = shortcut,
            };
            removeItem.Click += RemoveCustomShortcutMenuItem_Click;
            RemoveCustomShortcutMenu.Items.Add(removeItem);
        }
    }

    private async Task SendShortcutSafelyAsync(RemoteShortcut shortcut)
    {
        RemoteController? controller = _controller;
        if (controller?.IsConnected != true)
        {
            SetStatus("請先建立控制 session，再送出快捷鍵。");
            return;
        }

        try
        {
            await controller.SendShortcutAsync(shortcut);
            SetStatus($"已送出 {shortcut.GestureText} 到遠端目前作用中的應用程式。");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or ArgumentException)
        {
            SetStatus($"快捷鍵傳送失敗：{exception.Message}");
        }
    }

    private void FileTransferMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenFileTransferWindow();

    private void OpenFileTransferWindow(IEnumerable<string>? localSources = null)
    {
        RemoteController? controller = _controller;
        if (controller?.IsConnected != true)
        {
            SetStatus("請先建立控制 session，再開啟檔案傳輸。");
            return;
        }

        if (!controller.FileTransferAllowed)
        {
            SetStatus("被控端沒有開啟本次 session 的檔案傳輸權限；請斷線後重新核准。");
            return;
        }

        if (_fileTransferWindow is null || !_fileTransferWindow.IsLoaded)
        {
            _fileTransferWindow = new FileTransferWindow(controller)
            {
                Owner = this,
            };
            _fileTransferWindow.Closed += (_, _) => _fileTransferWindow = null;
            _fileTransferWindow.Show();
        }
        else
        {
            _fileTransferWindow.Activate();
        }

        if (localSources is not null)
        {
            _fileTransferWindow.AddLocalSources(localSources);
        }
    }

    private async void ReceiveRemoteClipboardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RemoteController? controller = _controller;
        if (controller?.IsConnected != true || !controller.FileTransferAllowed)
        {
            SetStatus("請先建立已允許檔案傳輸的控制 session。");
            return;
        }

        try
        {
            SetStatus("正在讀取遠端剪貼簿中的檔案清單…");
            IReadOnlyList<string> paths = await controller.GetRemoteClipboardFilesAsync();
            if (paths.Count == 0)
            {
                SetStatus("遠端剪貼簿目前沒有可傳輸的檔案或資料夾。");
                return;
            }

            string defaultDestination = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "LanRemote Incoming");
            OpenFolderDialog dialog = new()
            {
                Title = $"選擇 {paths.Count} 個遠端剪貼簿項目的接收位置",
                InitialDirectory = Directory.Exists(defaultDestination)
                    ? defaultDestination
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            if (dialog.ShowDialog(this) != true)
            {
                SetStatus("已取消接收遠端剪貼簿檔案。");
                return;
            }

            await RunDirectTransferAsync(
                FileTransferDirection.Download,
                token => controller.DownloadAsync(paths, dialog.FolderName, token));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus($"接收遠端剪貼簿失敗：{exception.Message}");
        }
    }

    private async Task RunDirectTransferAsync(
        FileTransferDirection direction,
        Func<CancellationToken, Task<FileTransferResult>> action)
    {
        if (_directTransferCancellation is not null)
        {
            SetStatus("已有一批直接拖放／剪貼簿傳輸正在進行；可從 Toolbar 取消。");
            return;
        }

        _directTransferCancellation = new CancellationTokenSource();
        _activeTransferDirection = direction;
        CancelActiveTransferToolButton.Visibility = Visibility.Visible;
        try
        {
            SetStatus(direction == FileTransferDirection.Upload
                ? "正在建立檔案 manifest 並上傳…"
                : "正在接收遠端檔案…");
            FileTransferResult result = await action(_directTransferCancellation.Token);
            SetStatus(result.Message);
        }
        catch (OperationCanceledException)
        {
            SetStatus("傳輸已取消；partial 已依你的選擇保留或清除。");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus($"檔案傳輸失敗：{exception.Message}");
        }
        finally
        {
            _directTransferCancellation.Dispose();
            _directTransferCancellation = null;
            _activeTransferId = null;
            StatusTransferText.Visibility = Visibility.Collapsed;
            CancelActiveTransferToolButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void CancelActiveTransferToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (_directTransferCancellation is null)
        {
            _fileTransferWindow?.Activate();
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "要刪除尚未完成的 partial 檔嗎？\n\n是：刪除\n否：保留供下次續傳",
            "取消檔案傳輸",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        if (_activeTransferId is Guid transferId && _controller is not null)
        {
            try
            {
                await _controller.CancelTransferAsync(
                    transferId,
                    _activeTransferDirection,
                    choice == MessageBoxResult.Yes);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                SetStatus($"取消要求未完整送達：{exception.Message}");
            }
        }

        _directTransferCancellation.Cancel();
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

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((key is Key.D0 or Key.NumPad0) &&
            modifiers.HasFlag(ModifierKeys.Control) &&
            modifiers.HasFlag(ModifierKeys.Alt))
        {
            _ = SendShortcutSafelyAsync(RemoteShortcut.ControlAltZero);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
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

    private void RemoteDisplay_DragEnter(object sender, DragEventArgs e)
    {
        if (!CanAcceptFileDrop(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        DropTargetText.Text = "正在解析遠端目的地…";
        DropTargetOverlay.Visibility = Visibility.Visible;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void RemoteDisplay_DragOver(object sender, DragEventArgs e)
    {
        if (!CanAcceptFileDrop(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        Point position = e.GetPosition(RemoteDisplay);
        if (!_dropResolveInProgress &&
            (DateTimeOffset.UtcNow - _lastDropResolveAt > TimeSpan.FromMilliseconds(450) ||
             (position - _lastDropResolvePoint).Length > 28))
        {
            await ResolveDropTargetAsync(position);
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void RemoteDisplay_DragLeave(object sender, DragEventArgs e)
    {
        DropTargetOverlay.Visibility = Visibility.Collapsed;
        _resolvedDropTarget = null;
    }

    private async void RemoteDisplay_Drop(object sender, DragEventArgs e)
    {
        DropTargetOverlay.Visibility = Visibility.Collapsed;
        if (!CanAcceptFileDrop(e) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        Point position = e.GetPosition(RemoteDisplay);
        if (string.IsNullOrWhiteSpace(_resolvedDropTarget))
        {
            await ResolveDropTargetAsync(position);
        }

        string? destination = _resolvedDropTarget;
        _resolvedDropTarget = null;
        if (string.IsNullOrWhiteSpace(destination))
        {
            SetStatus("無法可靠辨識放下位置；已開啟檔案傳輸視窗，請選擇遠端路徑。");
            OpenFileTransferWindow(paths);
            return;
        }

        SetStatus($"拖放目的地：{destination}");
        RemoteController controller = _controller!;
        await RunDirectTransferAsync(
            FileTransferDirection.Upload,
            token => controller.UploadAsync(paths, destination, token));
        e.Handled = true;
    }

    private bool CanAcceptFileDrop(DragEventArgs e) =>
        _controller?.IsConnected == true &&
        _controller.FileTransferAllowed &&
        e.Data.GetDataPresent(DataFormats.FileDrop);

    private async Task ResolveDropTargetAsync(Point position)
    {
        RemoteController? controller = _controller;
        if (controller?.IsConnected != true || !controller.FileTransferAllowed ||
            !TryNormalizePosition(position, out float x, out float y))
        {
            _resolvedDropTarget = null;
            DropTargetText.Text = "放開後選擇遠端目的路徑";
            return;
        }

        _dropResolveInProgress = true;
        _lastDropResolvePoint = position;
        _lastDropResolveAt = DateTimeOffset.UtcNow;
        try
        {
            DropTargetResponse response = await controller.ResolveDropTargetAsync(x, y);
            _resolvedDropTarget = response.Path;
            DropTargetText.Text = response.Path is null
                ? "無法辨識目前位置；放開後選擇遠端路徑"
                : $"傳送至：{response.Path}";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _resolvedDropTarget = null;
            DropTargetText.Text = $"無法辨識位置：{exception.Message}";
        }
        finally
        {
            _dropResolveInProgress = false;
        }
    }

    private async Task UploadLocalClipboardFilesAsync()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return;
        }

        string[] paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        Point position = Mouse.GetPosition(RemoteDisplay);
        await ResolveDropTargetAsync(position);
        string? destination = _resolvedDropTarget;
        _resolvedDropTarget = null;
        DropTargetOverlay.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(destination))
        {
            SetStatus("無法可靠辨識貼上位置；已開啟檔案傳輸視窗，請選擇遠端路徑。");
            OpenFileTransferWindow(paths);
            return;
        }

        RemoteController controller = _controller!;
        await RunDirectTransferAsync(
            FileTransferDirection.Upload,
            token => controller.UploadAsync(paths, destination, token));
    }

    private void RemoteDisplay_MouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(RemoteDisplay);
        if (!TryNormalizePosition(position, out float x, out float y))
        {
            ControllerCursorLayer.Visibility = Visibility.Collapsed;
            return;
        }

        ShowControllerCursor(position);
        if (_controller?.IsConnected != true || _mouseMoveThrottle.ElapsedMilliseconds < 12)
        {
            return;
        }

        _mouseMoveThrottle.Restart();
        SendInput(new RemoteInputEvent(RemoteInputKind.MouseMove, x, y));
    }

    private void RemoteDisplay_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_controller?.IsConnected == true)
        {
            ShowControllerCursor(e.GetPosition(RemoteDisplay));
        }
    }

    private void RemoteDisplay_MouseLeave(object sender, MouseEventArgs e) =>
        ControllerCursorLayer.Visibility = Visibility.Collapsed;

    private void ShowControllerCursor(Point position)
    {
        ControllerCursorLayer.Visibility = Visibility.Visible;
        Canvas.SetLeft(ControllerCursorArrow, position.X);
        Canvas.SetTop(ControllerCursorArrow, position.Y);
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
        if (key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            _controller.FileTransferAllowed && Clipboard.ContainsFileDropList())
        {
            _ = UploadLocalClipboardFilesAsync();
            e.Handled = true;
            return;
        }

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
        return RemoteViewportMapper.TryNormalizePoint(
            _scaleMode,
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
        ScaleModeComboBox.IsEnabled = session;
        SasToolButton.IsEnabled = session;
        CtrlAltZeroToolButton.IsEnabled = session;
        bool fileTransfer = session && _controller?.FileTransferAllowed == true;
        FileTransferMenuItem.IsEnabled = fileTransfer;
        ReceiveClipboardMenuItem.IsEnabled = fileTransfer;
        FileTransferToolButton.IsEnabled = fileTransfer;
        ReceiveClipboardToolButton.IsEnabled = fileTransfer;
        RebuildShortcutMenus();
        ModeBadge.Text = hosting ? "被控端模式" : controlling ? "控制端模式" : "尚未連線";
        UpdateStatusDisplay();
    }

    private static string FormatPairingCode(string code) =>
        code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;

    private void SetStatus(string message)
    {
        _lastStatusMessage = message;
        StatusText.Text = message;
        if (IsLoaded && !_closing)
        {
            TransientStatusBorder.Visibility = Visibility.Visible;
            _statusToastTimer.Stop();
            _statusToastTimer.Start();
        }

        UpdateStatusDisplay();
    }

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
        _statusToastTimer.Stop();
        _directTransferCancellation?.Cancel();
        _fileTransferWindow?.Close();
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

    private enum StatusDisplayMode
    {
        Off,
        Simple,
        Detailed,
    }
}
