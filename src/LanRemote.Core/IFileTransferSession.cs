using LanRemote.Protocol;

namespace LanRemote.Core;

public interface IFileTransferSession
{
    event Action<FileTransferProgress>? FileTransferProgressChanged;

    event Action<FileTransferResult>? FileTransferCompleted;

    bool IsConnected { get; }

    bool FileTransferAllowed { get; }

    Task<DirectoryBrowseResponse> BrowseRemoteAsync(
        string? path,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRemoteClipboardFilesAsync(
        CancellationToken cancellationToken = default);

    Task<FileTransferResult> UploadAsync(
        IReadOnlyList<string> sourcePaths,
        string remoteDestination,
        FileConflictBehavior conflictBehavior,
        CancellationToken cancellationToken = default);

    Task<FileTransferResult> DownloadAsync(
        IReadOnlyList<string> remoteSourcePaths,
        string localDestination,
        FileConflictBehavior conflictBehavior,
        CancellationToken cancellationToken = default);

    Task CancelTransferAsync(
        Guid transferId,
        FileTransferDirection direction,
        bool deletePartialFiles,
        CancellationToken cancellationToken = default);
}
