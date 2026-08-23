using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanRemote.Core;
using LanRemote.Protocol;
using Microsoft.Win32;

namespace LanRemote.App;

public partial class FileTransferPanel : UserControl
{
    private readonly ObservableCollection<string> _localSources = [];
    private readonly ObservableCollection<RemoteEntryRow> _remoteEntries = [];
    private readonly TransferSlot _upload = new(FileTransferDirection.Upload);
    private readonly TransferSlot _download = new(FileTransferDirection.Download);
    private IFileTransferSession? _session;

    public FileTransferPanel()
    {
        InitializeComponent();
        LocalSourcesList.ItemsSource = _localSources;
        RemoteEntriesList.ItemsSource = _remoteEntries;
        LocalDestinationTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LanRemote Incoming");
    }

    public bool HasActiveTransfers => _upload.Cancellation is not null || _download.Cancellation is not null;

    public event Action<bool>? ActiveTransfersChanged;

    public event Action? CloseRequested;

    public async Task SetSessionAsync(IFileTransferSession? session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        if (_session is not null)
        {
            _session.FileTransferProgressChanged -= Session_FileTransferProgressChanged;
        }

        _session = session;
        _remoteEntries.Clear();
        RemotePathTextBox.Text = string.Empty;
        PermissionText.Text = session?.IsConnected == true && session.FileTransferAllowed
            ? "本次連線已雙向允許"
            : "本次連線未允許";
        if (session?.IsConnected == true && session.FileTransferAllowed)
        {
            session.FileTransferProgressChanged += Session_FileTransferProgressChanged;
            await LoadRemoteDirectoryAsync(null);
        }
    }

    public void AddLocalSources(IEnumerable<string> paths)
    {
        foreach (string path in paths.Where(path => !_localSources.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            _localSources.Add(path);
        }
    }

    public async Task CancelAllFromToolbarAsync()
    {
        if (!HasActiveTransfers)
        {
            return;
        }

        MessageBoxResult choice = AskCancelChoice();
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        bool deletePartial = choice == MessageBoxResult.Yes;
        await CancelSlotAsync(_upload, deletePartial);
        await CancelSlotAsync(_download, deletePartial);
    }

    private void ClosePanelButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void ChooseFilesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = "選擇要傳送的檔案",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddLocalSources(dialog.FileNames);
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Multiselect = false,
            Title = "選擇要傳送的資料夾",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddLocalSources([dialog.FolderName]);
        }
    }

    private void ChooseLocalDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Multiselect = false,
            Title = "選擇下載目的資料夾",
            InitialDirectory = Directory.Exists(LocalDestinationTextBox.Text)
                ? LocalDestinationTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            LocalDestinationTextBox.Text = dialog.FolderName;
        }
    }

    private void ClearSourcesButton_Click(object sender, RoutedEventArgs e) => _localSources.Clear();

    private async void RemoteRootButton_Click(object sender, RoutedEventArgs e) =>
        await LoadRemoteDirectoryAsync(null);

    private async void RemoteUpButton_Click(object sender, RoutedEventArgs e)
    {
        string path = RemotePathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await LoadRemoteDirectoryAsync(Directory.GetParent(path)?.FullName);
        }
    }

    private async void RemoteGoButton_Click(object sender, RoutedEventArgs e) =>
        await LoadRemoteDirectoryAsync(RemotePathTextBox.Text.Trim());

    private async void RemotePathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadRemoteDirectoryAsync(RemotePathTextBox.Text.Trim());
            e.Handled = true;
        }
    }

    private async void RemoteEntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteEntriesList.SelectedItem is RemoteEntryRow { IsDirectory: true } row)
        {
            await LoadRemoteDirectoryAsync(row.FullPath);
        }
    }

    private async Task LoadRemoteDirectoryAsync(string? path)
    {
        IFileTransferSession? session = _session;
        if (session?.IsConnected != true || !session.FileTransferAllowed)
        {
            PermissionText.Text = "本次連線未允許";
            return;
        }

        try
        {
            DownloadStatusText.Text = "正在讀取對方路徑…";
            DirectoryBrowseResponse response = await session.BrowseRemoteAsync(path);
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                DownloadStatusText.Text = response.Error;
                return;
            }

            RemotePathTextBox.Text = response.Path;
            _remoteEntries.Clear();
            foreach (RemoteDirectoryEntry entry in response.Entries)
            {
                _remoteEntries.Add(new RemoteEntryRow(entry));
            }

            DownloadStatusText.Text = response.Truncated
                ? $"已顯示前 {response.Entries.Count} 個項目；清單已截斷。"
                : $"對方路徑共有 {response.Entries.Count} 個項目。";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            DownloadStatusText.Text = exception.Message;
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        IFileTransferSession? session = _session;
        if (session?.IsConnected != true || !session.FileTransferAllowed)
        {
            UploadStatusText.Text = "請先建立已允許檔案傳輸的連線。";
            return;
        }

        if (_localSources.Count == 0)
        {
            UploadStatusText.Text = "請先選擇本機檔案或資料夾。";
            return;
        }

        string destination = RemotePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            UploadStatusText.Text = "請先進入一個對方目的資料夾。";
            return;
        }

        FileConflictBehavior behavior = ResolveUploadConflictBehavior();
        await RunTransferAsync(
            _upload,
            token => session.UploadAsync(_localSources.ToArray(), destination, behavior, token));
        await LoadRemoteDirectoryAsync(destination);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        IFileTransferSession? session = _session;
        if (session?.IsConnected != true || !session.FileTransferAllowed)
        {
            DownloadStatusText.Text = "請先建立已允許檔案傳輸的連線。";
            return;
        }

        string[] paths = RemoteEntriesList.SelectedItems
            .OfType<RemoteEntryRow>()
            .Where(row => row.CanTransfer)
            .Select(row => row.FullPath)
            .ToArray();
        if (paths.Length == 0)
        {
            DownloadStatusText.Text = "請選擇可傳輸的對方檔案或資料夾。";
            return;
        }

        string destination = LocalDestinationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            DownloadStatusText.Text = "請選擇本機下載目的資料夾。";
            return;
        }

        Directory.CreateDirectory(destination);
        FileConflictBehavior behavior = ResolveDownloadConflictBehavior(paths, destination);
        await RunTransferAsync(
            _download,
            token => session.DownloadAsync(paths, destination, behavior, token));
    }

    private async Task RunTransferAsync(
        TransferSlot slot,
        Func<CancellationToken, Task<FileTransferResult>> action)
    {
        if (slot.Cancellation is not null)
        {
            SetSlotStatus(slot, "這個方向已有一批檔案正在傳輸。");
            return;
        }

        slot.TransferId = null;
        slot.Cancellation = new CancellationTokenSource();
        SetSlotEnabled(slot, true);
        SetSlotProgress(slot, 0);
        SetSlotStatus(slot, slot.Direction == FileTransferDirection.Upload
            ? "正在建立傳送 manifest 與 SHA-256…"
            : "正在等待對方建立接收 manifest…");
        ActiveTransfersChanged?.Invoke(true);
        try
        {
            FileTransferResult result = await action(slot.Cancellation.Token);
            SetSlotStatus(slot, result.Message);
            if (result.Succeeded)
            {
                SetSlotProgress(slot, 100);
            }
        }
        catch (OperationCanceledException)
        {
            SetSlotStatus(slot, "傳輸已取消；未刪除的 partial 可供下次續傳。");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetSlotStatus(slot, exception.Message);
        }
        finally
        {
            slot.Cancellation.Dispose();
            slot.Cancellation = null;
            slot.TransferId = null;
            SetSlotEnabled(slot, false);
            ActiveTransfersChanged?.Invoke(HasActiveTransfers);
        }
    }

    private void Session_FileTransferProgressChanged(FileTransferProgress progress)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            TransferSlot slot = progress.Direction == FileTransferDirection.Upload ? _upload : _download;
            if (slot.Cancellation is null)
            {
                return;
            }

            slot.TransferId ??= progress.TransferId;
            if (slot.TransferId != progress.TransferId)
            {
                return;
            }

            double percentage = progress.TotalBytes == 0
                ? 100
                : progress.TransferredBytes * 100d / progress.TotalBytes;
            SetSlotProgress(slot, Math.Clamp(percentage, 0, 100));
            SetSlotStatus(
                slot,
                $"{progress.State}｜{progress.CurrentItem}｜{FormatBytes(progress.TransferredBytes)} / " +
                FormatBytes(progress.TotalBytes));
        });
    }

    private async void CancelUploadButton_Click(object sender, RoutedEventArgs e) =>
        await CancelSlotWithPromptAsync(_upload);

    private async void CancelDownloadButton_Click(object sender, RoutedEventArgs e) =>
        await CancelSlotWithPromptAsync(_download);

    private async Task CancelSlotWithPromptAsync(TransferSlot slot)
    {
        MessageBoxResult choice = AskCancelChoice();
        if (choice != MessageBoxResult.Cancel)
        {
            await CancelSlotAsync(slot, choice == MessageBoxResult.Yes);
        }
    }

    private async Task CancelSlotAsync(TransferSlot slot, bool deletePartialFiles)
    {
        if (slot.Cancellation is null)
        {
            return;
        }

        if (slot.TransferId is Guid transferId && _session is not null)
        {
            try
            {
                await _session.CancelTransferAsync(
                    transferId,
                    slot.Direction,
                    deletePartialFiles);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                SetSlotStatus(slot, $"取消要求未完整送達：{exception.Message}");
            }
        }

        slot.Cancellation.Cancel();
    }

    private MessageBoxResult AskCancelChoice() => MessageBox.Show(
        Window.GetWindow(this),
        "要刪除尚未完成的 partial 檔嗎？\n\n是：取消並刪除\n否：取消但保留供續傳",
        "取消檔案傳輸",
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Question,
        MessageBoxResult.No);

    private FileConflictBehavior ResolveUploadConflictBehavior()
    {
        HashSet<string> remoteNames = _remoteEntries
            .Select(row => row.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] conflicts = _localSources
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(remoteNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return conflicts.Length == 0 ? FileConflictBehavior.KeepBoth : AskConflictBehavior(conflicts);
    }

    private static FileConflictBehavior ResolveDownloadConflictBehavior(
        IReadOnlyList<string> remotePaths,
        string destination)
    {
        string[] conflicts = remotePaths
            .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .Where(name => File.Exists(Path.Combine(destination, name)) ||
                           Directory.Exists(Path.Combine(destination, name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return conflicts.Length == 0 ? FileConflictBehavior.KeepBoth : AskConflictBehavior(conflicts);
    }

    private static FileConflictBehavior AskConflictBehavior(IReadOnlyList<string> conflicts)
    {
        string preview = string.Join("、", conflicts.Take(3));
        if (conflicts.Count > 3)
        {
            preview += $" 等 {conflicts.Count} 個項目";
        }

        MessageBoxResult choice = MessageBox.Show(
            $"目的地已有同名項目：{preview}\n\n是：覆寫\n否：保留兩者並自動改名（預設）\n取消：略過同名項目",
            "同名檔案處理",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return choice switch
        {
            MessageBoxResult.Yes => FileConflictBehavior.Overwrite,
            MessageBoxResult.Cancel => FileConflictBehavior.Skip,
            _ => FileConflictBehavior.KeepBoth,
        };
    }

    private void SetSlotProgress(TransferSlot slot, double value)
    {
        (slot.Direction == FileTransferDirection.Upload ? UploadProgressBar : DownloadProgressBar).Value = value;
    }

    private void SetSlotStatus(TransferSlot slot, string value)
    {
        (slot.Direction == FileTransferDirection.Upload ? UploadStatusText : DownloadStatusText).Text = value;
    }

    private void SetSlotEnabled(TransferSlot slot, bool value)
    {
        (slot.Direction == FileTransferDirection.Upload ? CancelUploadButton : CancelDownloadButton).IsEnabled = value;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }

    private sealed class TransferSlot(FileTransferDirection direction)
    {
        public FileTransferDirection Direction { get; } = direction;

        public CancellationTokenSource? Cancellation { get; set; }

        public Guid? TransferId { get; set; }
    }

    private sealed class RemoteEntryRow(RemoteDirectoryEntry entry)
    {
        public string Name { get; } = entry.Name;

        public string FullPath { get; } = entry.FullPath;

        public bool IsDirectory { get; } = entry.IsDirectory;

        public bool CanTransfer { get; } = entry.CanTransfer;

        public string DisplayType => !entry.CanTransfer
            ? "不支援"
            : entry.IsDirectory ? "資料夾" : "檔案";

        public string DisplaySize => entry.IsDirectory ? string.Empty : FormatBytes(entry.Length);

        public string ModifiedText => entry.LastWriteTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    }
}
