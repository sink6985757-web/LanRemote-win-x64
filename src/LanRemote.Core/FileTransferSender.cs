using LanRemote.Protocol;

namespace LanRemote.Core;

public static class FileTransferSender
{
    public static async Task SendAsync(
        PreparedFileTransfer transfer,
        IReadOnlyList<FileResumePoint> resumePoints,
        FramedMessageStream messages,
        MessageType chunkMessageType,
        FileTransferDirection direction,
        Action<FileTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        Dictionary<int, FileResumePoint> resumeByIndex = resumePoints.ToDictionary(point => point.EntryIndex);
        long transferred = resumePoints.Sum(point => point.Offset);
        byte[] buffer = new byte[ProtocolConstants.FileChunkLength];
        foreach (FileTransferEntry entry in transfer.Entries.Where(item => item.Kind == FileTransferEntryKind.File))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileResumePoint resume = resumeByIndex.GetValueOrDefault(entry.Index) ?? new(entry.Index, 0, false);
            if (resume.Offset < 0 || resume.Offset > entry.Length || (resume.Completed && resume.Offset != entry.Length))
            {
                throw new InvalidDataException("接收端回傳的續傳位置無效。");
            }

            if (resume.Completed)
            {
                continue;
            }

            string sourcePath = transfer.SourcePaths[entry.Index];
            await using FileStream stream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: ProtocolConstants.FileChunkLength,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != entry.Length)
            {
                throw new IOException($"來源檔案在建立 manifest 後發生變更：{sourcePath}");
            }

            stream.Position = resume.Offset;
            long offset = resume.Offset;
            while (offset < entry.Length)
            {
                int wanted = (int)Math.Min(buffer.Length, entry.Length - offset);
                int read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException($"來源檔案提前結束：{sourcePath}");
                }

                byte[] data = buffer.AsSpan(0, read).ToArray();
                FileChunkPayload chunk = new(transfer.TransferId, entry.Index, offset, data);
                await messages.WriteAsync(chunkMessageType, chunk.Serialize(), cancellationToken).ConfigureAwait(false);
                offset += read;
                transferred += read;
                progress?.Invoke(new FileTransferProgress(
                    transfer.TransferId,
                    direction,
                    entry.RelativePath,
                    transferred,
                    transfer.TotalBytes,
                    "傳輸中"));
            }
        }
    }
}
