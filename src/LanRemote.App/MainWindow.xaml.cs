using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private readonly LatestValueBuffer<string> _clipboardTextBuffer = new();
    private readonly DispatcherTimer _statusToastTimer = new()
    {
        Interval = TimeSpan.FromSeconds(4),
    };
    private readonly DispatcherTimer _cursorFadeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350),
    };
    private readonly object _inputQueueSync = new();
    private readonly HashSet<PhysicalKeyDescriptor> _remotePressedKeys = [];
    private Task _inputSendTail = Task.CompletedTask;
    private RemoteHost? _host;
    private RemoteController? _controller;
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
    private ClipboardSyncMode _clipboardSyncMode = ClipboardSyncMode.Bidirectional;
    private HwndSource? _windowSource;
    private bool _clipboardReadScheduled;
    private bool _clipboardReadRequested;
    private int _clipboardSendScheduled;
    private string? _recentRemoteClipboardHash;
    private DateTimeOffset _recentRemoteClipboardHashExpiresAt;
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
    private bool _remoteInputActive;
    private WindowsLowLevelKeyboardCapture? _keyboardCapture;

    public MainWindow()
    {
        InitializeComponent();
        FileTransferPanel.ActiveTransfersChanged += active =>
        {
            CancelActiveTransferToolButton.Visibility = active
                ? Visibility.Visible
                : _directTransferCancellation is not null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (active)
            {
                StatusTransferText.Text = "｜檔案傳輸中";
                StatusTransferText.Visibility = Visibility.Visible;
            }
        };
        FileTransferPanel.CloseRequested += ShowRemoteDesktopSurface;
        _statusToastTimer.Tick += (_, _) =>
        {
            _statusToastTimer.Stop();
            TransientStatusBorder.Visibility = Visibility.Collapsed;
        };
        _cursorFadeTimer.Tick += (_, _) =>
        {
            _cursorFadeTimer.Stop();
            ControllerCursorLayer.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                });
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        _keyboardCapture = new WindowsLowLevelKeyboardCapture(
            windowHandle,
            ForwardCapturedPhysicalKey,
            ReleaseRemoteKeysForLocalSecureAttention,
            HandleKeyboardCaptureFailure);
        if (_windowSource is not null && !AddClipboardFormatListener(_windowSource.Handle))
        {
            SetStatus("無法啟用 Windows 文字剪貼簿監聽；請重新啟動程式。");
        }
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
        host.SessionReadyReceived += ready => RunOnUi(() => EnterHostSessionView(ready));
        host.SessionEnded += () => RunOnUi(() =>
        {
            if (_host is not null)
            {
                LeaveSessionView();
                SetStatus("遠端連線已結束；被控端仍繼續等待下一次連線。");
                SetRoleUi();
            }
        });
        host.ClipboardTextReceived += text => RunOnUi(() => _ = ApplyRemoteClipboardTextAsync(text));
        host.ClipboardSyncModeChanged += mode => RunOnUi(() =>
        {
            if (mode == ClipboardSyncMode.Off)
            {
                ClearClipboardSyncState();
            }

            SetStatus(mode switch
            {
                ClipboardSyncMode.ControllerToHost => "本次連線已啟用單向文字剪貼簿（主控 → 被控）。",
                ClipboardSyncMode.Bidirectional => "本次連線已啟用雙向文字剪貼簿。",
                _ => "本次連線的文字剪貼簿同步已關閉。",
            });
        });
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

        ClearClipboardSyncState();
        LeaveSessionView();

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
            ShowConnectionFailure(exception.Message);
            return;
        }

        RemoteController controller = new(clipboardFileProvider: new WindowsClipboardFileProvider());
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
        controller.ClipboardTextReceived += text => RunOnUi(() => _ = ApplyRemoteClipboardTextAsync(text));
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
            await controller.ConnectAsync(
                endpoint,
                _selectedQualityPreset,
                RequestFileTransferCheckBox.IsChecked == true);
            _clipboardSyncMode = controller.ClipboardSyncMode;
            UpdateClipboardModeUi();
            SetRoleUi();
        }
        catch (Exception exception)
        {
            _controller = null;
            await controller.DisposeAsync();
            LeaveSessionView();
            SetRoleUi();
            ShowConnectionFailure(GetConnectionFailureReason(exception));
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
        ShowRemoteDesktopSurface();
        RemoteControllerSurface.Visibility = Visibility.Visible;
        HostSessionSurface.Visibility = Visibility.Collapsed;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        ApplyQualityUi(ready.QualityProfile);
        SetChromePinned(true);
        ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
        _ = FileTransferPanel.SetSessionAsync(_controller);
        SessionInfoText.Text = $"{ready.QualityProfile.DisplayName}｜{ready.Width}×{ready.Height}｜{ready.Codec}";
        SetStatus($"控制 session 已啟用；拖曳視窗即可自適應畫面，F11 切換全螢幕。");
        SetRoleUi();
        UpdateStatusDisplay();
    }

    private void EnterHostSessionView(SessionReady ready)
    {
        _sessionViewActive = true;
        LauncherView.Visibility = Visibility.Collapsed;
        SessionView.Visibility = Visibility.Visible;
        ShowRemoteDesktopSurface();
        RemoteControllerSurface.Visibility = Visibility.Collapsed;
        HostSessionSurface.Visibility = Visibility.Visible;
        SessionInfoText.Text = $"被控端｜{ready.Width}×{ready.Height}｜{ready.Codec}";
        SetChromePinned(true);
        ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
        _ = FileTransferPanel.SetSessionAsync(_host);
        SetStatus("已建立工作階段；需要傳輸檔案時請使用上方 Toolbar 的「檔案」。");
        SetRoleUi();
        UpdateStatusDisplay();
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is not null)
        {
            await DisconnectControllerAsync();
        }
        else if (_host?.IsConnected == true)
        {
            await _host.DisconnectSessionAsync();
        }
    }

    private async Task DisconnectControllerAsync()
    {
        RemoteController? controller = _controller;
        if (controller is not null)
        {
            DeactivateRemoteInput();
            await DrainInputQueueAsync();
            _controller = null;
            await controller.DisposeAsync();
        }
        else
        {
            _controller = null;
        }

        ClearClipboardSyncState();
        _clipboardSyncMode = ClipboardSyncMode.Bidirectional;
        UpdateClipboardModeUi();

        LeaveSessionView();
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

        DeactivateRemoteInput(sendRelease: false);
        _sessionViewActive = false;
        _ = FileTransferPanel.SetSessionAsync(null);
        SetChromePinned(true);
        RemoteDisplay.Source = null;
        _frameBuffer.Clear();
        Interlocked.Exchange(ref _framePresentationScheduled, 0);
        ControllerCursorLayer.Visibility = Visibility.Collapsed;
        RemoteDisplay.Visibility = Visibility.Collapsed;
        RemoteDisplayPlaceholder.Visibility = Visibility.Visible;
        FileTransferSurface.Visibility = Visibility.Collapsed;
        SessionDesktopSurface.Visibility = Visibility.Visible;
        SessionView.Visibility = Visibility.Collapsed;
        LauncherView.Visibility = Visibility.Visible;
        _remoteWidth = 0;
        _remoteHeight = 0;
        SessionInfoText.Text = "尚未連線";
        StatusTransferText.Visibility = Visibility.Collapsed;
        CancelActiveTransferToolButton.Visibility = Visibility.Collapsed;
        _resolvedDropTarget = null;
        DropTargetOverlay.Visibility = Visibility.Collapsed;
        UpdateRemoteInputUi();
        UpdateStatusDisplay();
    }

    private void EnqueueFrame(VideoFramePayload frame)
    {
        if (!_sessionViewActive)
        {
            return;
        }

        if (_controller?.IsConnected != true)
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
        bool controlling = _controller?.IsConnected == true;
        bool receivingControl = _host?.IsConnected == true;
        bool connected = controlling || receivingControl;
        StatusConnectionText.Text = connected
            ? "● 已連線"
            : hosting ? "● 等候連線" : "● 尚未連線";
        StatusConnectionText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            connected ? "#047857" : hosting ? "#B45309" : "#64748B"));
        StatusFpsText.Text = controlling ? $"｜{_currentPresentationFps:F1} FPS" : string.Empty;

        QualityProfile profile = _controller?.CurrentQualityProfile ?? QualityProfiles.Get(_selectedQualityPreset);
        SessionInfoText.Text = controlling
            ? $"｜目標 {profile.FramesPerSecond} FPS｜{_remoteWidth}×{_remoteHeight}"
            : receivingControl ? "｜被控端已連線" : hosting ? "｜被控端" : string.Empty;
        if (_lastMetrics is MetricsPayload metrics)
        {
            long age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastFrameTimestamp);
            MetricsText.Text = controlling
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
        CancelActiveTransferToolButton.Visibility = GetFileTransferSession() is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateStatusDisplay();
    }

    private void CompleteTransferStatus(FileTransferResult result)
    {
        _activeTransferId = null;
        CancelActiveTransferToolButton.Visibility = FileTransferPanel.HasActiveTransfers ||
                                                     _directTransferCancellation is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusTransferText.Visibility = FileTransferPanel.HasActiveTransfers ||
                                        _directTransferCancellation is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        => await SendShortcutSafelyAsync(shortcut, announce: true);

    private async Task SendShortcutSafelyAsync(RemoteShortcut shortcut, bool announce)
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
            if (announce)
            {
                SetStatus($"已送出 {shortcut.GestureText} 到遠端目前作用中的應用程式。");
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or ArgumentException)
        {
            SetStatus($"快捷鍵傳送失敗：{exception.Message}");
        }
    }

    private void FileTransferToolButton_Click(object sender, RoutedEventArgs e) =>
        OpenFileTransferWindow();

    private void OpenFileTransferWindow(IEnumerable<string>? localSources = null)
    {
        IFileTransferSession? session = GetFileTransferSession();
        if (session?.IsConnected != true)
        {
            SetStatus("請先建立已配對的工作階段，再開啟檔案傳輸。");
            return;
        }

        if (!session.FileTransferAllowed)
        {
            SetStatus("本次工作階段未取得雙方的檔案傳輸權限；請斷線後重新核准。");
            return;
        }

        if (localSources is not null)
        {
            FileTransferPanel.AddLocalSources(localSources);
        }

        DeactivateRemoteInput();
        SessionDesktopSurface.Visibility = Visibility.Collapsed;
        FileTransferSurface.Visibility = Visibility.Visible;
    }

    private IFileTransferSession? GetFileTransferSession() =>
        _controller?.IsConnected == true
            ? _controller
            : _host?.IsConnected == true
                ? _host
                : null;

    private void ShowRemoteDesktopSurface()
    {
        DeactivateRemoteInput();
        FileTransferSurface.Visibility = Visibility.Collapsed;
        SessionDesktopSurface.Visibility = Visibility.Visible;
    }

    private void ShowConnectionFailure(string reason)
    {
        string message = $"未連線成功\n\n{reason}";
        SetStatus($"未連線成功：{reason}");
        MessageBox.Show(
            this,
            message,
            "未連線成功",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string GetConnectionFailureReason(Exception exception) => exception switch
    {
        TimeoutException => exception.Message,
        UnauthorizedAccessException => exception.Message,
        ProtocolException => exception.Message,
        OperationCanceledException => "連線已取消或超過等待時間。",
        System.Net.Sockets.SocketException => $"無法連到指定的位址或連接埠：{exception.Message}",
        System.Security.Authentication.AuthenticationException => $"無法建立安全連線：{exception.Message}",
        _ => exception.Message,
    };

    private async void ReceiveRemoteClipboardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        IFileTransferSession? session = GetFileTransferSession();
        if (session?.IsConnected != true || !session.FileTransferAllowed)
        {
            SetStatus("請先建立已允許檔案傳輸的工作階段。");
            return;
        }

        try
        {
            SetStatus("正在讀取遠端剪貼簿中的檔案清單…");
            IReadOnlyList<string> paths = await session.GetRemoteClipboardFilesAsync();
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
                token => session.DownloadAsync(
                    paths,
                    dialog.FolderName,
                    FileConflictBehavior.KeepBoth,
                    token));
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
            StatusTransferText.Visibility = FileTransferPanel.HasActiveTransfers
                ? Visibility.Visible
                : Visibility.Collapsed;
            CancelActiveTransferToolButton.Visibility = FileTransferPanel.HasActiveTransfers
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void CancelActiveTransferToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (_directTransferCancellation is null)
        {
            if (FileTransferPanel.HasActiveTransfers)
            {
                await FileTransferPanel.CancelAllFromToolbarAsync();
            }
            else if (_activeTransferId is Guid activeTransferId &&
                     GetFileTransferSession() is { } activeSession)
            {
                MessageBoxResult remoteChoice = MessageBox.Show(
                    this,
                    "要取消目前傳輸並刪除尚未完成的 partial 檔嗎？\n\n是：刪除\n否：保留供續傳",
                    "取消檔案傳輸",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (remoteChoice != MessageBoxResult.Cancel)
                {
                    await activeSession.CancelTransferAsync(
                        activeTransferId,
                        _activeTransferDirection,
                        remoteChoice == MessageBoxResult.Yes);
                    _activeTransferId = null;
                }
            }
            else
            {
                OpenFileTransferWindow();
            }

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

        if (_activeTransferId is Guid transferId && GetFileTransferSession() is { } session)
        {
            try
            {
                await session.CancelTransferAsync(
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

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_remoteInputActive && !RemoteDisplay.IsMouseOver)
        {
            DeactivateRemoteInput();
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e) => DeactivateRemoteInput();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_sessionViewActive)
        {
            return;
        }

        if (_remoteInputActive)
        {
            // The low-level hook owns physical keys while locked. Injected fallback
            // events continue to the focused RemoteDisplay handlers below.
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        if (!_remoteInputActive &&
            (key is Key.D0 or Key.NumPad0) &&
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
        else if (e.Key == Key.Escape)
        {
            if (_remoteWindowMode == RemoteWindowMode.Fullscreen)
            {
                ApplyRemoteWindowMode(RemoteWindowMode.Windowed);
            }

            DeactivateRemoteInput();
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

    private void RemoteDisplay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_remoteInputActive)
        {
            HideControllerCursor();
            return;
        }

        Point position = e.GetPosition(RemoteDisplay);
        if (!TryNormalizePosition(position, out float x, out float y))
        {
            HideControllerCursor();
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
        if (_remoteInputActive && _controller?.IsConnected == true)
        {
            ShowControllerCursor(e.GetPosition(RemoteDisplay));
        }
    }

    private void RemoteDisplay_MouseLeave(object sender, MouseEventArgs e) => HideControllerCursor();

    private void ShowControllerCursor(Point position)
    {
        _cursorFadeTimer.Stop();
        ControllerCursorLayer.BeginAnimation(UIElement.OpacityProperty, null);
        ControllerCursorLayer.Opacity = 1;
        ControllerCursorLayer.Visibility = Visibility.Visible;
        Canvas.SetLeft(ControllerCursorArrow, position.X);
        Canvas.SetTop(ControllerCursorArrow, position.Y);
        _cursorFadeTimer.Start();
    }

    private void HideControllerCursor()
    {
        _cursorFadeTimer.Stop();
        ControllerCursorLayer.BeginAnimation(UIElement.OpacityProperty, null);
        ControllerCursorLayer.Opacity = 0;
        ControllerCursorLayer.Visibility = Visibility.Collapsed;
    }

    private void RemoteDisplay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_controller?.IsConnected != true || !TryMapButton(e.ChangedButton, out RemoteMouseButton button))
        {
            return;
        }

        ActivateRemoteInput();
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
        if (!_remoteInputActive ||
            _controller?.IsConnected != true ||
            !TryMapButton(e.ChangedButton, out RemoteMouseButton button))
        {
            return;
        }

        SendInput(new RemoteInputEvent(RemoteInputKind.MouseButton, Button: button, IsDown: false));
        Mouse.Capture(null);
        e.Handled = true;
    }

    private void RemoteDisplay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_remoteInputActive && _controller?.IsConnected == true)
        {
            SendInput(new RemoteInputEvent(RemoteInputKind.MouseWheel, WheelDelta: e.Delta));
            e.Handled = true;
        }
    }

    private void RemoteDisplay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_remoteInputActive || _controller?.IsConnected != true)
        {
            return;
        }

        if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            e.Handled = true;
            return;
        }

        Key key = ResolveKey(e);
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (WindowsKeyboardMapper.TryMapVirtualKey(virtualKey, out PhysicalKeyDescriptor physicalKey))
        {
            _remotePressedKeys.Add(physicalKey);
            SendInput(RemoteInputEvent.PhysicalKey(
                physicalKey.ScanCode,
                physicalKey.IsExtended,
                isDown: true));
            e.Handled = true;
        }
    }

    private void RemoteDisplay_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_remoteInputActive || _controller?.IsConnected != true)
        {
            return;
        }

        if (e.Key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            e.Handled = true;
            return;
        }

        Key key = ResolveKey(e);
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (WindowsKeyboardMapper.TryMapVirtualKey(virtualKey, out PhysicalKeyDescriptor physicalKey))
        {
            _remotePressedKeys.Remove(physicalKey);
            SendInput(RemoteInputEvent.PhysicalKey(
                physicalKey.ScanCode,
                physicalKey.IsExtended,
                isDown: false));
            e.Handled = true;
        }
    }

    private void ActivateRemoteInput()
    {
        if (_controller?.IsConnected != true ||
            !_sessionViewActive ||
            SessionDesktopSurface.Visibility != Visibility.Visible)
        {
            return;
        }

        _remoteInputActive = true;
        RemoteDisplay.Focus();
        try
        {
            _keyboardCapture?.Start();
        }
        catch (Win32Exception exception)
        {
            SetStatus($"實體鍵盤擷取未啟用，已退回視窗內輸入：{exception.Message}");
        }

        UpdateRemoteInputUi();
    }

    private void DeactivateRemoteInput(bool sendRelease = true)
    {
        bool hadRemoteInput = _remoteInputActive || _remotePressedKeys.Count > 0;
        _keyboardCapture?.Stop();
        _remoteInputActive = false;
        _remotePressedKeys.Clear();
        if (Mouse.Captured == RemoteDisplay)
        {
            Mouse.Capture(null);
        }

        HideControllerCursor();
        if (sendRelease && hadRemoteInput && _controller?.IsConnected == true)
        {
            SendInput(RemoteInputEvent.ReleaseAllKeys());
        }

        UpdateRemoteInputUi();
    }

    private void ForwardCapturedPhysicalKey(CapturedPhysicalKey key)
    {
        if (!_remoteInputActive || _controller?.IsConnected != true)
        {
            return;
        }

        PhysicalKeyDescriptor descriptor = new(key.ScanCode, key.IsExtended);
        if (key.IsDown)
        {
            _remotePressedKeys.Add(descriptor);
        }
        else
        {
            _remotePressedKeys.Remove(descriptor);
        }

        SendInput(RemoteInputEvent.PhysicalKey(
            key.ScanCode,
            key.IsExtended,
            key.IsDown));
    }

    private void ReleaseRemoteKeysForLocalSecureAttention()
    {
        _remotePressedKeys.Clear();
        if (_controller?.IsConnected == true)
        {
            SendInput(RemoteInputEvent.ReleaseAllKeys());
        }
    }

    private void HandleKeyboardCaptureFailure(Exception exception) =>
        RunOnUi(() => SetStatus($"實體鍵盤擷取發生錯誤，該按鍵已留在本機：{exception.Message}"));

    private void UpdateRemoteInputUi()
    {
        bool controllerSession = _sessionViewActive && _controller?.IsConnected == true;
        RemoteInputBorder.Visibility = controllerSession && _remoteInputActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusInputText.Visibility = controllerSession ? Visibility.Visible : Visibility.Collapsed;
        StatusInputText.Text = _remoteInputActive ? "｜遠端輸入" : "｜本機操作";
        StatusInputText.Foreground = _remoteInputActive
            ? new SolidColorBrush(Color.FromRgb(103, 232, 249))
            : new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }

    private static Key ResolveKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    private void SendInput(RemoteInputEvent inputEvent)
    {
        RemoteController? controller = _controller;
        if (controller is null)
        {
            return;
        }

        lock (_inputQueueSync)
        {
            Task previous = _inputSendTail;
            _inputSendTail = SendInputAfterAsync(previous, controller, inputEvent);
        }
    }

    private async Task SendInputAfterAsync(
        Task previous,
        RemoteController controller,
        RemoteInputEvent inputEvent)
    {
        try
        {
            await previous;
        }
        catch
        {
            // Keep the queue alive after a previously reported send failure.
        }

        await SendInputSafelyAsync(controller, inputEvent);
    }

    private Task DrainInputQueueAsync()
    {
        lock (_inputQueueSync)
        {
            return _inputSendTail;
        }
    }

    private async Task SendInputSafelyAsync(RemoteController controller, RemoteInputEvent inputEvent)
    {
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
        bool hostSession = _host?.IsConnected == true;
        bool controllerSession = _controller?.IsConnected == true;
        bool session = _sessionViewActive && (controllerSession || hostSession);
        StartHostButton.IsEnabled = !hosting && !controlling;
        StopHostButton.IsEnabled = hosting;
        ConnectButton.IsEnabled = !hosting && !controlling;
        DisconnectButton.IsEnabled = controllerSession || hostSession;
        HostPortTextBox.IsEnabled = !hosting && !controlling;
        RemoteEndpointTextBox.IsEnabled = !hosting && !controlling;
        StartHostMenuItem.IsEnabled = !hosting && !controlling;
        StopHostMenuItem.IsEnabled = hosting;
        ConnectMenuItem.IsEnabled = !hosting && !controlling;
        DisconnectMenuItem.IsEnabled = controllerSession || hostSession;
        ToolbarDisconnectButton.IsEnabled = controllerSession || hostSession;
        WindowedMenuItem.IsEnabled = controllerSession;
        MaximizedMenuItem.IsEnabled = controllerSession;
        FullscreenMenuItem.IsEnabled = controllerSession;
        ShowChromeMenuItem.IsEnabled = session;
        WindowedToolButton.IsEnabled = controllerSession;
        MaximizedToolButton.IsEnabled = controllerSession;
        FullscreenToolButton.IsEnabled = controllerSession;
        ScaleModeComboBox.IsEnabled = controllerSession;
        SasToolButton.IsEnabled = controllerSession;
        CtrlAltZeroToolButton.IsEnabled = controllerSession;
        bool fileTransfer = session && GetFileTransferSession()?.FileTransferAllowed == true;
        ReceiveClipboardMenuItem.IsEnabled = fileTransfer;
        FileTransferToolButton.IsEnabled = fileTransfer;
        ClipboardToolMenu.IsEnabled = controllerSession && _controller?.ClipboardTextAllowed == true;
        UpdateClipboardModeUi();
        RebuildShortcutMenus();
        ModeBadge.Text = hostSession
            ? "被控端已連線"
            : hosting
                ? "被控端等候中"
                : controlling
                    ? "控制端模式"
                    : "尚未連線";
        UpdateRemoteInputUi();
        UpdateStatusDisplay();
    }

    private async void ClipboardModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string modeName } ||
            !Enum.TryParse(modeName, out ClipboardSyncMode requestedMode))
        {
            return;
        }

        RemoteController? controller = _controller;
        if (controller?.IsConnected != true)
        {
            return;
        }

        try
        {
            await controller.ChangeClipboardSyncModeAsync(requestedMode);
            _clipboardSyncMode = requestedMode;
            if (requestedMode == ClipboardSyncMode.Off)
            {
                ClearClipboardSyncState();
            }

            UpdateClipboardModeUi();
            SetStatus(requestedMode switch
            {
                ClipboardSyncMode.ControllerToHost => "文字剪貼簿：單向（主控 → 被控）。",
                ClipboardSyncMode.Bidirectional => "文字剪貼簿：雙向同步。",
                _ => "文字剪貼簿同步已關閉；遠端內部的 Ctrl+C／X／V 仍可使用。",
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            _clipboardSyncMode = controller.ClipboardSyncMode;
            UpdateClipboardModeUi();
            SetStatus($"剪貼簿模式切換失敗：{exception.Message}");
        }

        RemoteDisplay.Focus();
    }

    private void UpdateClipboardModeUi()
    {
        if (!IsInitialized)
        {
            return;
        }

        ClipboardOffMenuItem.IsChecked = _clipboardSyncMode == ClipboardSyncMode.Off;
        ClipboardOneWayMenuItem.IsChecked = _clipboardSyncMode == ClipboardSyncMode.ControllerToHost;
        ClipboardBidirectionalMenuItem.IsChecked = _clipboardSyncMode == ClipboardSyncMode.Bidirectional;
        ClipboardToolMenu.ToolTip = _clipboardSyncMode switch
        {
            ClipboardSyncMode.ControllerToHost => "純文字剪貼簿：單向（主控 → 被控）",
            ClipboardSyncMode.Bidirectional => "純文字剪貼簿：雙向",
            _ => "純文字剪貼簿：關閉",
        };
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WindowMessageClipboardUpdate)
        {
            ScheduleClipboardRead();
        }

        return IntPtr.Zero;
    }

    private void ScheduleClipboardRead()
    {
        if (!ShouldMonitorLocalClipboard())
        {
            return;
        }

        _clipboardReadRequested = true;
        if (_clipboardReadScheduled)
        {
            return;
        }

        _clipboardReadScheduled = true;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                do
                {
                    _clipboardReadRequested = false;
                    await ReadLocalClipboardTextAsync();
                }
                while (_clipboardReadRequested && ShouldMonitorLocalClipboard());
            }
            finally
            {
                _clipboardReadScheduled = false;
                if (_clipboardReadRequested && ShouldMonitorLocalClipboard())
                {
                    ScheduleClipboardRead();
                }
            }
        }, DispatcherPriority.Background);
    }

    private bool ShouldMonitorLocalClipboard() =>
        (_controller?.IsConnected == true &&
         _controller.ClipboardTextAllowed &&
         _clipboardSyncMode != ClipboardSyncMode.Off) ||
        _host?.CanSendClipboardText == true;

    private async Task ReadLocalClipboardTextAsync()
    {
        for (int attempt = 0; attempt < 4 && ShouldMonitorLocalClipboard(); attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList() ||
                    !Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    return;
                }

                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (text.Length == 0)
                {
                    return;
                }

                string hash = ComputeClipboardTextHash(text);
                if (hash == _recentRemoteClipboardHash &&
                    DateTimeOffset.UtcNow <= _recentRemoteClipboardHashExpiresAt)
                {
                    return;
                }

                _recentRemoteClipboardHash = null;
                _clipboardTextBuffer.Offer(text);
                ScheduleClipboardSend();
                return;
            }
            catch (ExternalException) when (attempt < 3)
            {
                await Task.Delay(35);
            }
        }
    }

    private void ScheduleClipboardSend()
    {
        if (Interlocked.CompareExchange(ref _clipboardSendScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = DrainClipboardTextAsync();
    }

    private async Task DrainClipboardTextAsync()
    {
        try
        {
            while (_clipboardTextBuffer.Take() is string text)
            {
                if (_controller?.IsConnected == true &&
                    _controller.ClipboardTextAllowed &&
                    _clipboardSyncMode != ClipboardSyncMode.Off)
                {
                    await _controller.SendClipboardTextAsync(text);
                }
                else if (_host?.CanSendClipboardText == true)
                {
                    await _host.SendClipboardTextAsync(text);
                }
            }
        }
        catch (ArgumentException exception)
        {
            RunOnUi(() => SetStatus(exception.Message));
        }
        catch (Exception exception) when (
            exception is IOException or OperationCanceledException or ObjectDisposedException or ProtocolException)
        {
            if (!_closing)
            {
                RunOnUi(() => SetStatus($"文字剪貼簿同步中斷：{exception.Message}"));
            }
        }
        finally
        {
            Interlocked.Exchange(ref _clipboardSendScheduled, 0);
            if (_clipboardTextBuffer.HasValue && ShouldMonitorLocalClipboard())
            {
                ScheduleClipboardSend();
            }
        }
    }

    private async Task ApplyRemoteClipboardTextAsync(string text)
    {
        if (_closing || text.Length == 0)
        {
            return;
        }

        string hash = ComputeClipboardTextHash(text);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsFileDropList() &&
                    Clipboard.ContainsText(TextDataFormat.UnicodeText) &&
                    Clipboard.GetText(TextDataFormat.UnicodeText) == text)
                {
                    return;
                }

                _recentRemoteClipboardHash = hash;
                _recentRemoteClipboardHashExpiresAt = DateTimeOffset.UtcNow.AddSeconds(2);
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return;
            }
            catch (ExternalException) when (attempt < 3)
            {
                await Task.Delay(35);
            }
            catch (ExternalException exception)
            {
                _recentRemoteClipboardHash = null;
                SetStatus($"無法寫入 Windows 剪貼簿：{exception.Message}");
                return;
            }
        }
    }

    private void ClearClipboardSyncState()
    {
        _clipboardTextBuffer.Clear();
        _clipboardReadRequested = false;
        _recentRemoteClipboardHash = null;
        _recentRemoteClipboardHashExpiresAt = default;
    }

    private static string ComputeClipboardTextHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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

    protected override void OnClosed(EventArgs e)
    {
        ClearClipboardSyncState();
        _keyboardCapture?.Dispose();
        _keyboardCapture = null;
        if (_windowSource is not null)
        {
            _ = RemoveClipboardFormatListener(_windowSource.Handle);
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }

        base.OnClosed(e);
    }

    private const int WindowMessageClipboardUpdate = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

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
