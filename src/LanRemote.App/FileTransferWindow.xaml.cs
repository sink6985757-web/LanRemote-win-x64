using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using LanRemote.Core;
using LanRemote.Protocol;
using Microsoft.Win32;

namespace LanRemote.App;

public partial class FileTransferWindow : Window
{
    private readonly RemoteController _controller;
    private readonly ObservableCollection<string> _localSources = [];
    private readonly ObservableCollection<RemoteEntryRow> _remoteEntries = [];
    private CancellationTokenSource? _transferCancellation;
    private Guid? _activeTransferId;
    private FileTransferDirection _activeDirection;
    private bool _closing;

    public FileTransferWindow(RemoteController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        InitializeComponent();
        LocalSourcesList.ItemsSource = _localSources;
        RemoteEntriesList.ItemsSource = _remoteEntries;
        string downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LanRemote Incoming");
        LocalDestinationTextBox.Text = downloads;
        _controller.FileTransferProgressChanged += Controller_FileTransferProgressChanged;
        Loaded += async (_, _) => await LoadRemoteDirectoryAsync(null);
    }

    public void AddLocalSources(IEnumerable<string> paths)
    {
        foreach (string path in paths.Where(path => !_localSources.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            _localSources.Add(path);
        }
    }

    private void ChooseFilesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = "選擇要傳送的檔案",
        };
        if (dialog.ShowDialog(this) == true)
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
        if (dialog.ShowDialog(this) == true)
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
        if (dialog.ShowDialog(this) == true)
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
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await LoadRemoteDirectoryAsync(Directory.GetParent(path)?.FullName);
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
        try
        {
            TransferStatusText.Text = "正在讀取遠端路徑…";
            DirectoryBrowseResponse response = await _controller.BrowseRemoteAsync(path);
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                TransferStatusText.Text = response.Error;
                return;
            }

            RemotePathTextBox.Text = response.Path;
            _remoteEntries.Clear();
            foreach (RemoteDirectoryEntry entry in response.Entries)
            {
                _remoteEntries.Add(new RemoteEntryRow(entry));
            }

            TransferStatusText.Text = response.Truncated
                ? $"已顯示前 {response.Entries.Count} 個項目；清單已截斷。"
                : $"遠端路徑共有 {response.Entries.Count} 個項目。";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            TransferStatusText.Text = exception.Message;
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_localSources.Count == 0)
        {
            TransferStatusText.Text = "請先選擇本機檔案或資料夾。";
            return;
        }

        string destination = RemotePathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            TransferStatusText.Text = "請先進入一個遠端目的資料夾。";
            return;
        }

        await RunTransferAsync(
            FileTransferDirection.Upload,
            token => _controller.UploadAsync(_localSources.ToArray(), destination, token));
        await LoadRemoteDirectoryAsync(destination);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        string[] paths = RemoteEntriesList.SelectedItems
            .OfType<RemoteEntryRow>()
            .Where(row => row.CanTransfer)
            .Select(row => row.FullPath)
            .ToArray();
        if (paths.Length == 0)
        {
            TransferStatusText.Text = "請選擇可傳輸的遠端檔案或資料夾。";
            return;
        }

        string destination = LocalDestinationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            TransferStatusText.Text = "請選擇本機下載目的資料夾。";
            return;
        }

        Directory.CreateDirectory(destination);
        await RunTransferAsync(
            FileTransferDirection.Download,
            token => _controller.DownloadAsync(paths, destination, token));
    }

    private async Task RunTransferAsync(
        FileTransferDirection direction,
        Func<CancellationToken, Task<FileTransferResult>> action)
    {
        if (_transferCancellation is not null)
        {
            TransferStatusText.Text = "已有一批檔案正在傳輸。";
            return;
        }

        _activeDirection = direction;
        _activeTransferId = null;
        _transferCancellation = new CancellationTokenSource();
        CancelTransferButton.IsEnabled = true;
        TransferProgressBar.Value = 0;
        TransferStatusText.Text = direction == FileTransferDirection.Upload
            ? "正在建立上傳 manifest 與 SHA-256…"
            : "正在等待遠端建立下載 manifest…";
        try
        {
            FileTransferResult result = await action(_transferCancellation.Token);
            TransferStatusText.Text = result.Message;
            if (result.Succeeded)
            {
                TransferProgressBar.Value = 100;
            }
        }
        catch (OperationCanceledException)
        {
            TransferStatusText.Text = "傳輸已取消；未刪除的 partial 可供下次續傳。";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            TransferStatusText.Text = exception.Message;
        }
        finally
        {
            _transferCancellation.Dispose();
            _transferCancellation = null;
            _activeTransferId = null;
            CancelTransferButton.IsEnabled = false;
        }
    }

    private void Controller_FileTransferProgressChanged(FileTransferProgress progress)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _activeTransferId = progress.TransferId;
            _activeDirection = progress.Direction;
            double percentage = progress.TotalBytes == 0
                ? 100
                : progress.TransferredBytes * 100d / progress.TotalBytes;
            TransferProgressBar.Value = Math.Clamp(percentage, 0, 100);
            TransferStatusText.Text =
                $"{progress.State}｜{progress.CurrentItem}｜{FormatBytes(progress.TransferredBytes)} / " +
                FormatBytes(progress.TotalBytes);
        });
    }

    private async void CancelTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transferCancellation is null)
        {
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            this,
            "要刪除這批傳輸尚未完成的 partial 檔嗎？\n\n是：取消並刪除 partial\n否：取消但保留，下次可續傳",
            "取消檔案傳輸",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        if (_activeTransferId is Guid transferId)
        {
            try
            {
                await _controller.CancelTransferAsync(
                    transferId,
                    _activeDirection,
                    choice == MessageBoxResult.Yes);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                TransferStatusText.Text = $"取消要求未完整送達：{exception.Message}";
            }
        }

        _transferCancellation.Cancel();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        if (_transferCancellation is not null)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "傳輸仍在進行；要取消並保留 partial 供續傳嗎？",
                "關閉檔案傳輸視窗",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _transferCancellation.Cancel();
        }

        _closing = true;
        _controller.FileTransferProgressChanged -= Controller_FileTransferProgressChanged;
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
