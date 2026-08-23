namespace LanRemote.Protocol;

public static class ProtocolConstants
{
    public const byte Version = 6;
    public const int HeaderLength = 10;
    public const int MaxPayloadLength = 16 * 1024 * 1024;
    public const int FileChunkLength = 256 * 1024;
    public const int ClipboardTextChunkLength = 256 * 1024;
    public const int MaxClipboardTextLength = 64 * 1024 * 1024;
    public const int MaxTransferEntries = 10_000;
    public const long MaxFileLength = 2L * 1024 * 1024 * 1024;
    public const long MaxBatchLength = 10L * 1024 * 1024 * 1024;
    public const int NonceLength = 32;
    public static ReadOnlySpan<byte> Magic => "LRM6"u8;
}
