using System.Security.Cryptography;
using LanRemote.Protocol;

namespace LanRemote.Core;

public sealed record PreparedFileTransfer(
    Guid TransferId,
    IReadOnlyList<FileTransferEntry> Entries,
    IReadOnlyDictionary<int, string> SourcePaths,
    long TotalBytes,
    string ManifestSha256);

public static class FileTransferManifestBuilder
{
    public static async Task<PreparedFileTransfer> CreateAsync(
        IReadOnlyList<string> sourcePaths,
        Guid? transferId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        if (sourcePaths.Count == 0)
        {
            throw new InvalidOperationException("請至少選擇一個檔案或資料夾。");
        }

        List<FileTransferEntry> entries = [];
        Dictionary<int, string> sources = [];
        HashSet<string> relativePaths = new(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (string requestedPath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = FileTransferPolicy.ValidateSourcePath(requestedPath);
            string topName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
            if (File.Exists(fullPath))
            {
                await AddFileAsync(fullPath, topName).ConfigureAwait(false);
                continue;
            }

            AddDirectory(fullPath, topName);
            foreach (string child in Directory.EnumerateFileSystemEntries(
                         fullPath,
                         "*",
                         new EnumerationOptions
                         {
                             RecurseSubdirectories = true,
                             IgnoreInaccessible = false,
                             AttributesToSkip = 0,
                         }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(child);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException($"不支援 reparse point：{child}");
                }

                string relative = Path.Combine(topName, Path.GetRelativePath(fullPath, child));
                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    AddDirectory(child, relative);
                }
                else
                {
                    await AddFileAsync(child, relative).ConfigureAwait(false);
                }
            }
        }

        string fingerprint = FileTransferPolicy.ComputeManifestFingerprint(entries);
        FileTransferPolicy.ValidateManifest(entries, totalBytes, fingerprint);
        return new PreparedFileTransfer(
            transferId ?? Guid.NewGuid(),
            entries,
            sources,
            totalBytes,
            fingerprint);

        void AddDirectory(string source, string relative)
        {
            string normalized = FileTransferPolicy.NormalizeRelativePath(relative);
            if (!relativePaths.Add(normalized))
            {
                throw new InvalidDataException($"傳輸來源產生重複路徑：{normalized}");
            }

            EnsureEntryCapacity(entries.Count + 1);
            int index = entries.Count;
            entries.Add(new FileTransferEntry(index, normalized, FileTransferEntryKind.Directory, 0, string.Empty));
            sources[index] = source;
        }

        async Task AddFileAsync(string source, string relative)
        {
            string normalized = FileTransferPolicy.NormalizeRelativePath(relative);
            if (!relativePaths.Add(normalized))
            {
                throw new InvalidDataException($"傳輸來源產生重複路徑：{normalized}");
            }

            FileInfo info = new(source);
            if (info.Length > ProtocolConstants.MaxFileLength)
            {
                throw new InvalidDataException($"單一檔案超過 2 GB：{source}");
            }

            checked
            {
                totalBytes += info.Length;
            }

            if (totalBytes > ProtocolConstants.MaxBatchLength)
            {
                throw new InvalidDataException("傳輸批次超過 10 GB 上限。");
            }

            EnsureEntryCapacity(entries.Count + 1);
            await using FileStream stream = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            int index = entries.Count;
            entries.Add(new FileTransferEntry(
                index,
                normalized,
                FileTransferEntryKind.File,
                info.Length,
                Convert.ToHexString(hash)));
            sources[index] = source;
        }
    }

    private static void EnsureEntryCapacity(int count)
    {
        if (count > ProtocolConstants.MaxTransferEntries)
        {
            throw new InvalidDataException(
                $"傳輸項目超過 {ProtocolConstants.MaxTransferEntries} 個上限。");
        }
    }
}
