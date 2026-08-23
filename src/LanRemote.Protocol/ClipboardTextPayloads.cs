using System.Buffers.Binary;

namespace LanRemote.Protocol;

public enum ClipboardSyncMode : byte
{
    Off = 0,
    ControllerToHost = 1,
    Bidirectional = 2,
}

public sealed record ClipboardModeChanged(ClipboardSyncMode Mode);

public sealed record ClipboardTextOffer(
    Guid UpdateId,
    int Utf8ByteCount,
    string Sha256);

public sealed record ClipboardTextComplete(Guid UpdateId);

public sealed record ClipboardTextChunkPayload(
    Guid UpdateId,
    int Offset,
    byte[] Data)
{
    private const int HeaderLength = 24;

    public byte[] Serialize()
    {
        Validate();
        byte[] payload = new byte[HeaderLength + Data.Length];
        UpdateId.TryWriteBytes(payload.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16, 4), Offset);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(20, 4), Data.Length);
        Data.CopyTo(payload.AsSpan(HeaderLength));
        return payload;
    }

    public static ClipboardTextChunkPayload Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderLength)
        {
            throw new ProtocolException("Clipboard text chunk payload is truncated.");
        }

        Guid updateId = new(payload[..16]);
        int offset = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(16, 4));
        int dataLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(20, 4));
        if (dataLength < 0 || dataLength > ProtocolConstants.ClipboardTextChunkLength ||
            payload.Length != HeaderLength + dataLength)
        {
            throw new ProtocolException("Clipboard text chunk length is invalid.");
        }

        ClipboardTextChunkPayload chunk = new(
            updateId,
            offset,
            payload[HeaderLength..].ToArray());
        chunk.Validate();
        return chunk;
    }

    private void Validate()
    {
        if (UpdateId == Guid.Empty || Offset < 0)
        {
            throw new ProtocolException("Clipboard text chunk identity or offset is invalid.");
        }

        if (Data.Length > ProtocolConstants.ClipboardTextChunkLength)
        {
            throw new ProtocolException(
                $"Clipboard text chunk exceeds {ProtocolConstants.ClipboardTextChunkLength} bytes.");
        }
    }
}
