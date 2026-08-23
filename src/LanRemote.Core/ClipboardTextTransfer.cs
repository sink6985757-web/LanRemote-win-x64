using System.Security.Cryptography;
using System.Text;
using LanRemote.Protocol;

namespace LanRemote.Core;

public sealed class ClipboardTextReceiver
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private ClipboardTextOffer? _offer;
    private byte[]? _buffer;
    private int _received;

    public bool HasPendingUpdate => _offer is not null;

    public void Begin(ClipboardTextOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ValidateOffer(offer);
        if (_offer is not null)
        {
            throw new ProtocolException("A clipboard text update is already in progress.");
        }

        _offer = offer;
        _buffer = new byte[offer.Utf8ByteCount];
        _received = 0;
    }

    public void Append(ClipboardTextChunkPayload chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (_offer is null || _buffer is null || chunk.UpdateId != _offer.UpdateId)
        {
            throw new ProtocolException("Clipboard text chunk does not match the active update.");
        }

        if (chunk.Offset != _received || chunk.Data.Length == 0 ||
            chunk.Data.Length > _buffer.Length - _received)
        {
            throw new ProtocolException("Clipboard text chunk offset or length is invalid.");
        }

        chunk.Data.CopyTo(_buffer.AsSpan(_received));
        _received += chunk.Data.Length;
    }

    public string Complete(ClipboardTextComplete complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        if (_offer is null || _buffer is null || complete.UpdateId != _offer.UpdateId)
        {
            throw new ProtocolException("Clipboard text completion does not match the active update.");
        }

        try
        {
            if (_received != _buffer.Length)
            {
                throw new ProtocolException("Clipboard text update is incomplete.");
            }

            byte[] expectedHash = Convert.FromHexString(_offer.Sha256);
            byte[] actualHash = SHA256.HashData(_buffer);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new ProtocolException("Clipboard text integrity verification failed.");
            }

            return StrictUtf8.GetString(_buffer);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ProtocolException($"Clipboard text is not valid UTF-8: {exception.Message}");
        }
        finally
        {
            Reset();
        }
    }

    public void Reset()
    {
        _offer = null;
        _buffer = null;
        _received = 0;
    }

    public static void ValidateMode(ClipboardSyncMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ProtocolException($"Unknown clipboard synchronization mode {(byte)mode}.");
        }
    }

    private static void ValidateOffer(ClipboardTextOffer offer)
    {
        if (offer.UpdateId == Guid.Empty || offer.Utf8ByteCount < 0 ||
            offer.Utf8ByteCount > ProtocolConstants.MaxClipboardTextLength)
        {
            throw new ProtocolException("Clipboard text update identity or size is invalid.");
        }

        if (offer.Sha256.Length != 64)
        {
            throw new ProtocolException("Clipboard text SHA-256 is invalid.");
        }

        try
        {
            if (Convert.FromHexString(offer.Sha256).Length != 32)
            {
                throw new ProtocolException("Clipboard text SHA-256 is invalid.");
            }
        }
        catch (FormatException exception)
        {
            throw new ProtocolException($"Clipboard text SHA-256 is invalid: {exception.Message}");
        }
    }
}

public static class ClipboardTextSender
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task SendAsync(
        FramedMessageStream messages,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(text);

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException($"剪貼簿文字包含無效的 Unicode 字元：{exception.Message}", nameof(text));
        }

        if (bytes.Length > ProtocolConstants.MaxClipboardTextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"剪貼簿文字超過 {ProtocolConstants.MaxClipboardTextLength / 1024 / 1024} MB 上限。");
        }

        Guid updateId = Guid.NewGuid();
        ClipboardTextOffer offer = new(
            updateId,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)));
        await messages.WriteAsync(
            MessageType.ClipboardTextOffer,
            PayloadJson.Serialize(offer),
            cancellationToken).ConfigureAwait(false);

        for (int offset = 0; offset < bytes.Length; offset += ProtocolConstants.ClipboardTextChunkLength)
        {
            int length = Math.Min(ProtocolConstants.ClipboardTextChunkLength, bytes.Length - offset);
            byte[] chunkBytes = bytes.AsSpan(offset, length).ToArray();
            ClipboardTextChunkPayload chunk = new(updateId, offset, chunkBytes);
            await messages.WriteAsync(
                MessageType.ClipboardTextChunk,
                chunk.Serialize(),
                cancellationToken).ConfigureAwait(false);
        }

        await messages.WriteAsync(
            MessageType.ClipboardTextComplete,
            PayloadJson.Serialize(new ClipboardTextComplete(updateId)),
            cancellationToken).ConfigureAwait(false);
    }
}
