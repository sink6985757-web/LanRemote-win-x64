using System.Security.Cryptography;
using System.Text.Json;
using LanRemote.Protocol;

namespace LanRemote.Core;

public sealed class FileTransferReceiver
{
    private readonly string _stateRoot;
    private readonly Dictionary<Guid, ActiveTransfer> _active = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileTransferReceiver(string? stateRoot = null)
    {
        _stateRoot = stateRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanRemote",
            "Transfers");
    }

    public Task<FileTransferDecision> AcceptUploadAsync(
        FileUploadOffer offer,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(
            offer.TransferId,
            offer.DestinationPath,
            offer.Entries,
            offer.TotalBytes,
            offer.ManifestSha256,
            cancellationToken);

    public Task<FileTransferDecision> AcceptDownloadAsync(
        FileDownloadOffer offer,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(
            offer.TransferId,
            destinationPath,
            offer.Entries,
            offer.TotalBytes,
            offer.ManifestSha256,
            cancellationToken);

    public async Task<FileTransferProgress> ReceiveChunkAsync(
        FileChunkPayload chunk,
        FileTransferDirection direction,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveTransfer transfer = GetActive(chunk.TransferId);
            FileTransferEntry entry = transfer.EntriesByIndex.GetValueOrDefault(chunk.EntryIndex)
                ?? throw new InvalidDataException("File chunk references an unknown entry.");
            if (entry.Kind != FileTransferEntryKind.File || transfer.State.CompletedIndices.Contains(entry.Index))
            {
                throw new InvalidDataException("File chunk references a completed or non-file entry.");
            }

            string finalPath = transfer.State.ResolvedPaths[entry.Index];
            string partialPath = GetPartialPath(finalPath, chunk.TransferId);
            Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
            long existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (chunk.Offset != existingLength || chunk.Offset + chunk.Data.Length > entry.Length)
            {
                throw new InvalidDataException("File chunk offset or length does not match the partial file.");
            }

            await using FileStream stream = new(
                partialPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None,
                bufferSize: ProtocolConstants.FileChunkLength,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            stream.Position = chunk.Offset;
            await stream.WriteAsync(chunk.Data, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            long transferred = CalculateTransferredBytes(transfer);
            return new FileTransferProgress(
                chunk.TransferId,
                direction,
                entry.RelativePath,
                transferred,
                transfer.TotalBytes,
                "傳輸中");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FileTransferResult> CompleteAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveTransfer transfer = GetActive(transferId);
            foreach (FileTransferEntry entry in transfer.Entries.OrderBy(item => item.Index))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string finalPath = transfer.State.ResolvedPaths[entry.Index];
                if (entry.Kind == FileTransferEntryKind.Directory)
                {
                    Directory.CreateDirectory(finalPath);
                    continue;
                }

                if (transfer.State.CompletedIndices.Contains(entry.Index))
                {
                    continue;
                }

                string partialPath = GetPartialPath(finalPath, transferId);
                if (!File.Exists(partialPath) || new FileInfo(partialPath).Length != entry.Length)
                {
                    throw new InvalidDataException($"檔案尚未完整接收：{entry.RelativePath}");
                }

                await using (FileStream stream = new(
                                 partialPath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 bufferSize: 1024 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(
                            hash,
                            Convert.FromHexString(entry.Sha256)))
                    {
                        throw new InvalidDataException($"SHA-256 驗證失敗：{entry.RelativePath}");
                    }
                }

                if (File.Exists(finalPath) || Directory.Exists(finalPath))
                {
                    throw new IOException($"目的地在傳輸期間出現同名項目，未覆寫：{finalPath}");
                }

                File.Move(partialPath, finalPath);
                transfer.State.CompletedIndices.Add(entry.Index);
                await SaveStateAsync(transfer.State, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<string> paths = GetTopLevelPaths(transfer);
            DeleteStateFile(transferId);
            _active.Remove(transferId);
            return new FileTransferResult(transferId, true, "檔案傳輸完成並通過 SHA-256 驗證。", paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _active.Remove(transferId);
            return new FileTransferResult(transferId, false, exception.Message, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync(
        Guid transferId,
        bool deletePartialFiles,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_active.Remove(transferId, out ActiveTransfer? transfer))
            {
                return;
            }

            if (!deletePartialFiles)
            {
                return;
            }

            foreach (FileTransferEntry entry in transfer.Entries.Where(
                         item => item.Kind == FileTransferEntryKind.File &&
                                 !transfer.State.CompletedIndices.Contains(item.Index)))
            {
                string finalPath = transfer.State.ResolvedPaths[entry.Index];
                string partialPath = GetPartialPath(finalPath, transferId);
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
            }

            DeleteStateFile(transferId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SuspendAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _active.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileTransferDecision> AcceptAsync(
        Guid transferId,
        string destinationPath,
        IReadOnlyList<FileTransferEntry> entries,
        long totalBytes,
        string manifestSha256,
        CancellationToken cancellationToken)
    {
        if (transferId == Guid.Empty)
        {
            return Rejected(transferId, "Transfer ID 無效。");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active.ContainsKey(transferId))
            {
                return Rejected(transferId, "相同傳輸已在進行中。");
            }

            try
            {
                FileTransferPolicy.ValidateManifest(entries, totalBytes, manifestSha256);
                string destination = FileTransferPolicy.ValidateExistingDirectory(destinationPath);
                ReceiverState? state = await LoadStateAsync(transferId, cancellationToken).ConfigureAwait(false);
                if (state is not null &&
                    (!string.Equals(state.DestinationPath, destination, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(state.ManifestSha256, manifestSha256, StringComparison.OrdinalIgnoreCase)))
                {
                    return Rejected(transferId, "續傳資料與本次目的地或 manifest 不一致。");
                }

                state ??= CreateState(transferId, destination, entries, manifestSha256);
                ActiveTransfer active = new(state, entries, totalBytes);
                ReconcileCompletedFiles(active);
                PrepareDirectories(active);
                await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);

                List<FileResumePoint> resumePoints = [];
                foreach (FileTransferEntry entry in entries.Where(item => item.Kind == FileTransferEntryKind.File))
                {
                    bool completed = state.CompletedIndices.Contains(entry.Index);
                    string partialPath = GetPartialPath(state.ResolvedPaths[entry.Index], transferId);
                    long offset = completed ? entry.Length : File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                    if (offset > entry.Length)
                    {
                        return Rejected(transferId, $"Partial 檔案長度超過來源：{entry.RelativePath}");
                    }

                    resumePoints.Add(new FileResumePoint(entry.Index, offset, completed));
                }

                _active.Add(transferId, active);
                return new FileTransferDecision(transferId, true, null, resumePoints);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Rejected(transferId, exception.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static FileTransferDecision Rejected(Guid transferId, string reason) =>
        new(transferId, false, reason, []);

    private ReceiverState CreateState(
        Guid transferId,
        string destination,
        IReadOnlyList<FileTransferEntry> entries,
        string manifestSha256)
    {
        Dictionary<string, string> topLevelMappings = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> resolvedPaths = [];
        foreach (FileTransferEntry entry in entries.OrderBy(item => item.RelativePath.Count(character => character is '/' or '\\')))
        {
            string normalized = FileTransferPolicy.NormalizeRelativePath(entry.RelativePath);
            string[] segments = normalized.Split(Path.DirectorySeparatorChar);
            string topLevel = segments[0];
            if (!topLevelMappings.TryGetValue(topLevel, out string? resolvedTopLevel))
            {
                resolvedTopLevel = GetAvailableName(destination, topLevel, entry.Kind == FileTransferEntryKind.Directory);
                topLevelMappings[topLevel] = resolvedTopLevel;
            }

            segments[0] = resolvedTopLevel;
            resolvedPaths[entry.Index] = FileTransferPolicy.CombineUnderRoot(destination, Path.Combine(segments));
        }

        return new ReceiverState(
            transferId,
            destination,
            manifestSha256,
            resolvedPaths,
            []);
    }

    private static string GetAvailableName(string destination, string requestedName, bool isDirectory)
    {
        string candidate = requestedName;
        string extension = isDirectory ? string.Empty : Path.GetExtension(requestedName);
        string stem = isDirectory ? requestedName : Path.GetFileNameWithoutExtension(requestedName);
        for (int suffix = 1; File.Exists(Path.Combine(destination, candidate)) ||
                             Directory.Exists(Path.Combine(destination, candidate)); suffix++)
        {
            candidate = $"{stem} ({suffix}){extension}";
        }

        return candidate;
    }

    private static void PrepareDirectories(ActiveTransfer transfer)
    {
        foreach (FileTransferEntry entry in transfer.Entries.OrderBy(item => item.RelativePath.Length))
        {
            string finalPath = transfer.State.ResolvedPaths[entry.Index];
            if (entry.Kind == FileTransferEntryKind.Directory)
            {
                Directory.CreateDirectory(finalPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                string partialPath = GetPartialPath(finalPath, transfer.State.TransferId);
                if (!File.Exists(partialPath) && entry.Length == 0)
                {
                    using FileStream _ = File.Create(partialPath);
                }
            }
        }
    }

    private static void ReconcileCompletedFiles(ActiveTransfer transfer)
    {
        foreach (FileTransferEntry entry in transfer.Entries.Where(item => item.Kind == FileTransferEntryKind.File))
        {
            if (!transfer.State.CompletedIndices.Contains(entry.Index))
            {
                continue;
            }

            string finalPath = transfer.State.ResolvedPaths[entry.Index];
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length != entry.Length)
            {
                transfer.State.CompletedIndices.Remove(entry.Index);
            }
        }
    }

    private static long CalculateTransferredBytes(ActiveTransfer transfer)
    {
        long total = 0;
        foreach (FileTransferEntry entry in transfer.Entries.Where(item => item.Kind == FileTransferEntryKind.File))
        {
            if (transfer.State.CompletedIndices.Contains(entry.Index))
            {
                total += entry.Length;
                continue;
            }

            string partialPath = GetPartialPath(transfer.State.ResolvedPaths[entry.Index], transfer.State.TransferId);
            if (File.Exists(partialPath))
            {
                total += Math.Min(entry.Length, new FileInfo(partialPath).Length);
            }
        }

        return total;
    }

    private static IReadOnlyList<string> GetTopLevelPaths(ActiveTransfer transfer) =>
        transfer.Entries
            .Select(entry => transfer.State.ResolvedPaths[entry.Index])
            .Where(path => !transfer.State.ResolvedPaths.Values.Any(other =>
                !string.Equals(path, other, StringComparison.OrdinalIgnoreCase) &&
                path.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private ActiveTransfer GetActive(Guid transferId) =>
        _active.GetValueOrDefault(transferId)
        ?? throw new InvalidOperationException("找不到進行中的檔案傳輸。");

    private static string GetPartialPath(string finalPath, Guid transferId) =>
        $"{finalPath}.{transferId:N}.lanremote.partial";

    private string GetStatePath(Guid transferId) => Path.Combine(_stateRoot, $"{transferId:N}.json");

    private async Task<ReceiverState?> LoadStateAsync(Guid transferId, CancellationToken cancellationToken)
    {
        string path = GetStatePath(transferId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ReceiverState>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SaveStateAsync(ReceiverState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_stateRoot);
        string path = GetStatePath(state.TransferId);
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private void DeleteStateFile(Guid transferId)
    {
        string path = GetStatePath(transferId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ReceiverState(
        Guid TransferId,
        string DestinationPath,
        string ManifestSha256,
        Dictionary<int, string> ResolvedPaths,
        HashSet<int> CompletedIndices);

    private sealed class ActiveTransfer(
        ReceiverState state,
        IReadOnlyList<FileTransferEntry> entries,
        long totalBytes)
    {
        public ReceiverState State { get; } = state;

        public IReadOnlyList<FileTransferEntry> Entries { get; } = entries;

        public IReadOnlyDictionary<int, FileTransferEntry> EntriesByIndex { get; } =
            entries.ToDictionary(entry => entry.Index);

        public long TotalBytes { get; } = totalBytes;
    }
}
