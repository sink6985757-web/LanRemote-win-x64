using System.Buffers.Binary;

namespace LanRemote.Protocol;

public enum VideoCodec : byte
{
    Jpeg = 1,
    H264 = 2,
}

public sealed record VideoFramePayload(
    int Width,
    int Height,
    long CapturedAtUnixMilliseconds,
    VideoCodec Codec,
    byte[] Data)
{
    private const int MetadataLength = 17;

    public byte[] Serialize()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        ArgumentNullException.ThrowIfNull(Data);
        if (Data.Length + MetadataLength > ProtocolConstants.MaxPayloadLength)
        {
            throw new ProtocolException("Encoded video frame exceeds the protocol limit.");
        }

        byte[] payload = new byte[MetadataLength + Data.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), Width);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), Height);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(8, 8), CapturedAtUnixMilliseconds);
        payload[16] = (byte)Codec;
        Data.CopyTo(payload, MetadataLength);
        return payload;
    }

    public static VideoFramePayload Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= MetadataLength)
        {
            throw new ProtocolException("Video frame payload is incomplete.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(payload[0..4]);
        int height = BinaryPrimitives.ReadInt32BigEndian(payload[4..8]);
        long timestamp = BinaryPrimitives.ReadInt64BigEndian(payload[8..16]);
        VideoCodec codec = (VideoCodec)payload[16];
        if (width <= 0 || height <= 0 || !Enum.IsDefined(codec))
        {
            throw new ProtocolException("Video frame metadata is invalid.");
        }

        return new VideoFramePayload(width, height, timestamp, codec, payload[MetadataLength..].ToArray());
    }
}
