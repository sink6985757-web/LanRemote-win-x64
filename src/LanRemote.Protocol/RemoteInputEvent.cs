using System.Buffers.Binary;

namespace LanRemote.Protocol;

public enum RemoteInputKind : byte
{
    MouseMove = 1,
    MouseButton = 2,
    MouseWheel = 3,
    Key = 4,
    PhysicalKey = 5,
    UnicodeText = 6,
    ReleaseAllKeys = 7,
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
    ushort VirtualKey = 0,
    ushort ScanCode = 0,
    bool IsExtended = false,
    int UnicodeScalar = 0)
{
    private const int PayloadLength = 24;

    public static RemoteInputEvent PhysicalKey(ushort scanCode, bool isExtended, bool isDown) =>
        new(RemoteInputKind.PhysicalKey, IsDown: isDown, ScanCode: scanCode, IsExtended: isExtended);

    public static RemoteInputEvent UnicodeText(int unicodeScalar) =>
        new(RemoteInputKind.UnicodeText, UnicodeScalar: unicodeScalar);

    public static RemoteInputEvent ReleaseAllKeys() => new(RemoteInputKind.ReleaseAllKeys);

    public byte[] Serialize()
    {
        Validate();
        byte[] payload = new byte[PayloadLength];
        payload[0] = (byte)Kind;
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(1, 4), NormalizedX);
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(5, 4), NormalizedY);
        payload[9] = (byte)Button;
        payload[10] = IsDown ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(11, 4), WheelDelta);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(15, 2), VirtualKey);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(17, 2), ScanCode);
        payload[19] = IsExtended ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(20, 4), UnicodeScalar);
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
        ushort scanCode = BinaryPrimitives.ReadUInt16BigEndian(payload[17..19]);
        bool isExtended = payload[19] == 1;
        int unicodeScalar = BinaryPrimitives.ReadInt32BigEndian(payload[20..24]);

        if (payload[10] > 1 || payload[19] > 1)
        {
            throw new ProtocolException("Input event flags are invalid.");
        }

        RemoteInputEvent result = new(
            kind,
            x,
            y,
            button,
            isDown,
            wheel,
            virtualKey,
            scanCode,
            isExtended,
            unicodeScalar);
        result.Validate();
        return result;
    }

    private void Validate()
    {
        if (Kind == RemoteInputKind.MouseMove &&
            (float.IsNaN(NormalizedX) || float.IsNaN(NormalizedY) ||
             NormalizedX is < 0 or > 1 || NormalizedY is < 0 or > 1))
        {
            throw new ProtocolException("Mouse coordinates must be normalized to the [0,1] range.");
        }

        if (Kind == RemoteInputKind.MouseButton && !Enum.IsDefined(Button))
        {
            throw new ProtocolException("Mouse button is invalid.");
        }

        if (Kind == RemoteInputKind.Key && VirtualKey == 0)
        {
            throw new ProtocolException("Virtual-key input requires a key code.");
        }

        if (Kind == RemoteInputKind.PhysicalKey && ScanCode == 0)
        {
            throw new ProtocolException("Physical-key input requires a scan code.");
        }

        if (Kind == RemoteInputKind.UnicodeText &&
            (UnicodeScalar == 0 || !System.Text.Rune.IsValid(UnicodeScalar)))
        {
            throw new ProtocolException("Unicode input requires a valid scalar value.");
        }
    }
}
