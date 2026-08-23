namespace LanRemote.Protocol;

public static class ProtocolConstants
{
    public const byte Version = 4;
    public const int HeaderLength = 10;
    public const int MaxPayloadLength = 16 * 1024 * 1024;
    public const int FileChunkLength = 256 * 1024;
    public const int MaxTransferEntries = 10_000;
    public const long MaxFileLength = 2L * 1024 * 1024 * 1024;
    public const long MaxBatchLength = 10L * 1024 * 1024 * 1024;
    public const int NonceLength = 32;
    public static ReadOnlySpan<byte> Magic => "LRM4"u8;
}
