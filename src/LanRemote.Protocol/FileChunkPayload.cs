using System.Buffers.Binary;

namespace LanRemote.Protocol;

public sealed record FileChunkPayload(
    Guid TransferId,
    int EntryIndex,
    long Offset,
    byte[] Data)
{
    private const int HeaderLength = 32;

    public byte[] Serialize()
    {
        Validate();
        byte[] payload = new byte[HeaderLength + Data.Length];
        TransferId.TryWriteBytes(payload.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16, 4), EntryIndex);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(20, 8), Offset);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(28, 4), Data.Length);
        Data.CopyTo(payload.AsSpan(HeaderLength));
        return payload;
    }

    public static FileChunkPayload Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderLength)
        {
            throw new ProtocolException("File chunk payload is truncated.");
        }

        Guid transferId = new(payload[..16]);
        int entryIndex = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(16, 4));
        long offset = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(20, 8));
        int dataLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(28, 4));
        if (dataLength < 0 || dataLength > ProtocolConstants.FileChunkLength ||
            payload.Length != HeaderLength + dataLength)
        {
            throw new ProtocolException("File chunk length is invalid.");
        }

        FileChunkPayload chunk = new(transferId, entryIndex, offset, payload[HeaderLength..].ToArray());
        chunk.Validate();
        return chunk;
    }

    private void Validate()
    {
        if (TransferId == Guid.Empty || EntryIndex < 0 || Offset < 0)
        {
            throw new ProtocolException("File chunk identity or offset is invalid.");
        }

        if (Data.Length > ProtocolConstants.FileChunkLength)
        {
            throw new ProtocolException($"File chunk exceeds {ProtocolConstants.FileChunkLength} bytes.");
        }
    }
}
