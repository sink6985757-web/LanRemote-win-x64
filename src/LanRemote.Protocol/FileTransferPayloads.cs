namespace LanRemote.Protocol;

public enum FileTransferEntryKind
{
    File = 1,
    Directory = 2,
}

public enum FileTransferDirection
{
    Upload = 1,
    Download = 2,
}

public enum FileConflictBehavior
{
    KeepBoth = 1,
    Overwrite = 2,
    Skip = 3,
}

public sealed record FileTransferEntry(
    int Index,
    string RelativePath,
    FileTransferEntryKind Kind,
    long Length,
    string Sha256);

public sealed record FileResumePoint(int EntryIndex, long Offset, bool Completed);

public sealed record DirectoryBrowseRequest(Guid RequestId, string? Path);

public sealed record RemoteDirectoryEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Length,
    DateTimeOffset LastWriteTime,
    bool CanTransfer);

public sealed record DirectoryBrowseResponse(
    Guid RequestId,
    string Path,
    IReadOnlyList<RemoteDirectoryEntry> Entries,
    bool Truncated,
    string? Error);

public sealed record DropTargetRequest(Guid RequestId, float NormalizedX, float NormalizedY);

public sealed record DropTargetResponse(Guid RequestId, string? Path, string? Error);

public sealed record ClipboardFilesRequest(Guid RequestId);

public sealed record ClipboardFilesResponse(
    Guid RequestId,
    IReadOnlyList<string> Paths,
    string? Error);

public sealed record FileUploadOffer(
    Guid TransferId,
    string DestinationPath,
    IReadOnlyList<FileTransferEntry> Entries,
    long TotalBytes,
    string ManifestSha256,
    FileConflictBehavior ConflictBehavior = FileConflictBehavior.KeepBoth);

public sealed record FileTransferDecision(
    Guid TransferId,
    bool Accepted,
    string? Reason,
    IReadOnlyList<FileResumePoint> ResumePoints);

public sealed record FileTransferComplete(Guid TransferId);

public sealed record FileTransferResult(
    Guid TransferId,
    bool Succeeded,
    string Message,
    IReadOnlyList<string> CompletedPaths);

public sealed record FileDownloadRequest(
    Guid RequestId,
    Guid TransferId,
    IReadOnlyList<string> SourcePaths);

public sealed record FileDownloadOffer(
    Guid RequestId,
    Guid TransferId,
    IReadOnlyList<FileTransferEntry> Entries,
    long TotalBytes,
    string ManifestSha256,
    string? Error = null);

public sealed record FileTransferCancel(
    Guid TransferId,
    FileTransferDirection Direction,
    bool DeletePartialFiles);

public sealed record FileTransferProgress(
    Guid TransferId,
    FileTransferDirection Direction,
    string CurrentItem,
    long TransferredBytes,
    long TotalBytes,
    string State);
