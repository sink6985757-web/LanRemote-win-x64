namespace LanRemote.Protocol;

public static class ProtocolConstants
{
    public const byte Version = 2;
    public const int HeaderLength = 10;
    public const int MaxPayloadLength = 16 * 1024 * 1024;
    public const int NonceLength = 32;
    public static ReadOnlySpan<byte> Magic => "LRM2"u8;
}
