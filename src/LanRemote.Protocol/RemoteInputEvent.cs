using System.Buffers.Binary;

namespace LanRemote.Protocol;

public enum RemoteInputKind : byte
{
    MouseMove = 1,
    MouseButton = 2,
    MouseWheel = 3,
    Key = 4,
}

public enum RemoteMouseButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3,
}

public sealed record RemoteInputEvent(
    RemoteInputKind Kind,
    float NormalizedX = 0,
    float NormalizedY = 0,
    RemoteMouseButton Button = RemoteMouseButton.Left,
    bool IsDown = false,
    int WheelDelta = 0,
    ushort VirtualKey = 0)
{
    private const int PayloadLength = 17;

    public byte[] Serialize()
    {
        byte[] payload = new byte[PayloadLength];
        payload[0] = (byte)Kind;
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(1, 4), NormalizedX);
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(5, 4), NormalizedY);
        payload[9] = (byte)Button;
        payload[10] = IsDown ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(11, 4), WheelDelta);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(15, 2), VirtualKey);
        return payload;
    }

    public static RemoteInputEvent Deserialize(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != PayloadLength)
        {
            throw new ProtocolException("Input event payload has an invalid length.");
        }

        RemoteInputKind kind = (RemoteInputKind)payload[0];
        if (!Enum.IsDefined(kind))
        {
            throw new ProtocolException("Input event kind is invalid.");
        }

        float x = BinaryPrimitives.ReadSingleBigEndian(payload[1..5]);
        float y = BinaryPrimitives.ReadSingleBigEndian(payload[5..9]);
        RemoteMouseButton button = (RemoteMouseButton)payload[9];
        bool isDown = payload[10] == 1;
        int wheel = BinaryPrimitives.ReadInt32BigEndian(payload[11..15]);
        ushort virtualKey = BinaryPrimitives.ReadUInt16BigEndian(payload[15..17]);

        if (kind == RemoteInputKind.MouseMove &&
            (float.IsNaN(x) || float.IsNaN(y) || x is < 0 or > 1 || y is < 0 or > 1))
        {
            throw new ProtocolException("Mouse coordinates must be normalized to the [0,1] range.");
        }

        return new RemoteInputEvent(kind, x, y, button, isDown, wheel, virtualKey);
    }
}
