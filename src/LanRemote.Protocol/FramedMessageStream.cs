using System.Buffers.Binary;

namespace LanRemote.Protocol;

public sealed class FramedMessageStream : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _disposed;

    public FramedMessageStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException("The protocol stream must be readable and writable.", nameof(stream));
        }

        _stream = stream;
    }

    public async ValueTask WriteAsync(
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (payload.Length > ProtocolConstants.MaxPayloadLength)
        {
            throw new ProtocolException($"Payload exceeds {ProtocolConstants.MaxPayloadLength} bytes.");
        }

        byte[] header = new byte[ProtocolConstants.HeaderLength];
        ProtocolConstants.Magic.CopyTo(header);
        header[4] = ProtocolConstants.Version;
        header[5] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(6, 4), payload.Length);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask<ProtocolPacket> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] header = new byte[ProtocolConstants.HeaderLength];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual(ProtocolConstants.Magic))
        {
            throw new ProtocolException("Invalid protocol magic.");
        }

        if (header[4] != ProtocolConstants.Version)
        {
            throw new ProtocolException($"Unsupported protocol version {header[4]}.");
        }

        MessageType type = (MessageType)header[5];
        if (!Enum.IsDefined(type))
        {
            throw new ProtocolException($"Unknown message type {header[5]}.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(6, 4));
        if (payloadLength < 0 || payloadLength > ProtocolConstants.MaxPayloadLength)
        {
            throw new ProtocolException($"Invalid payload length {payloadLength}.");
        }

        byte[] payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new ProtocolPacket(type, payload);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _writeGate.Dispose();
        return _stream.DisposeAsync();
    }
}
