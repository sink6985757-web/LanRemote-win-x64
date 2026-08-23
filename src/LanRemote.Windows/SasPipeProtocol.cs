using System.IO.Pipes;
using System.Text;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Windows;

public static class SasPipeProtocol
{
    public const string PipeName = "LanRemote.Sas.v1";
    public static ReadOnlySpan<byte> RequestMagic => "LRSAS1"u8;
    public const byte SecureAttentionCommand = 1;
    private const int MaximumMessageBytes = 1024;

    public static async ValueTask WriteRequestAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] request = new byte[RequestMagic.Length + 1];
        RequestMagic.CopyTo(request);
        request[^1] = SecureAttentionCommand;
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask ReadAndValidateRequestAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] request = new byte[RequestMagic.Length + 1];
        await stream.ReadExactlyAsync(request, cancellationToken).ConfigureAwait(false);
        if (!request.AsSpan(0, RequestMagic.Length).SequenceEqual(RequestMagic) ||
            request[^1] != SecureAttentionCommand)
        {
            throw new InvalidDataException("Invalid LanRemote SAS request.");
        }
    }

    public static async ValueTask WriteResultAsync(
        Stream stream,
        bool succeeded,
        string message,
        CancellationToken cancellationToken = default)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        if (messageBytes.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("LanRemote SAS result is too long.");
        }

        byte[] header = new byte[3];
        header[0] = succeeded ? (byte)1 : (byte)0;
        BitConverter.TryWriteBytes(header.AsSpan(1), checked((ushort)messageBytes.Length));
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(messageBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<(bool Succeeded, string Message)> ReadResultAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[3];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BitConverter.ToUInt16(header, 1);
        if (length > MaximumMessageBytes)
        {
            throw new InvalidDataException("LanRemote SAS result is too long.");
        }

        byte[] messageBytes = new byte[length];
        await stream.ReadExactlyAsync(messageBytes, cancellationToken).ConfigureAwait(false);
        return (header[0] == 1, Encoding.UTF8.GetString(messageBytes));
    }
}

public sealed class NamedPipeSecureAttentionProvider : ISecureAttentionProvider
{
    public async ValueTask<SecureAttentionResult> RequestAsync(
        SecureAttentionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await using NamedPipeClientStream pipe = new(
                ".",
                SasPipeProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await SasPipeProtocol.WriteRequestAsync(pipe, timeout.Token).ConfigureAwait(false);
            (bool succeeded, string message) =
                await SasPipeProtocol.ReadResultAsync(pipe, timeout.Token).ConfigureAwait(false);
            return new SecureAttentionResult(request.RequestId, succeeded, message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return new SecureAttentionResult(
                request.RequestId,
                false,
                "無法使用 SAS 服務。請在被控端以系統管理員身分執行 install-sas-service.ps1，" +
                $"並確認服務已啟動。詳細：{exception.Message}");
        }
    }
}
