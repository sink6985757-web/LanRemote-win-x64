using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LanRemote.Protocol;

namespace LanRemote.Core;

public sealed class RemoteHost : IAsyncDisposable
{
    private readonly IScreenFrameSource _screenFrameSource;
    private readonly IInputInjector _inputInjector;
    private readonly CancellationTokenSource _lifetime = new();
    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private Task? _acceptLoop;

    public RemoteHost(IScreenFrameSource screenFrameSource, IInputInjector inputInjector)
    {
        _screenFrameSource = screenFrameSource ?? throw new ArgumentNullException(nameof(screenFrameSource));
        _inputInjector = inputInjector ?? throw new ArgumentNullException(nameof(inputInjector));
    }

    public Func<PairingRequest, CancellationToken, Task<bool>>? PairingApprovalHandler { get; set; }

    public event Action<string>? StatusChanged;

    public event Action<MetricsPayload>? MetricsChanged;

    public IPEndPoint? ListeningEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Remote host is already running.");
        }

        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        cancellationToken.ThrowIfCancellationRequested();
        _certificate = EphemeralCertificate.Create();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start(backlog: 4);
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
        OnStatus($"正在等候連線，連接埠 {ListeningEndpoint?.Port}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                break;
            }
            catch (Exception exception)
            {
                client.Dispose();
                OnStatus($"連線已結束：{exception.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken hostCancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndpoint ||
                !LanAddressPolicy.IsAllowed(remoteEndpoint.Address))
            {
                OnStatus("已拒絕非區網來源。");
                return;
            }

            OnStatus($"收到 {remoteEndpoint.Address} 的加密連線。");
            using SslStream sslStream = new(client.GetStream(), leaveInnerStreamOpen: false);
            SslServerAuthenticationOptions authenticationOptions = new()
            {
                ServerCertificate = _certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };
            await sslStream.AuthenticateAsServerAsync(authenticationOptions, hostCancellationToken)
                .ConfigureAwait(false);

            await using FramedMessageStream messages = new(sslStream);
            ProtocolPacket helloPacket = await messages.ReadAsync(hostCancellationToken).ConfigureAwait(false);
            if (helloPacket.Type != MessageType.ClientHello)
            {
                throw new ProtocolException("The first client message must be ClientHello.");
            }

            ClientHello hello = PayloadJson.Deserialize<ClientHello>(helloPacket.Payload);
            if (hello.ProtocolVersion != ProtocolConstants.Version)
            {
                throw new ProtocolException("Client protocol version is not supported.");
            }

            byte[] clientNonce = DecodeNonce(hello.NonceBase64);
            byte[] serverNonce = RandomNumberGenerator.GetBytes(ProtocolConstants.NonceLength);
            ServerHello serverHello = new(
                ProtocolConstants.Version,
                Environment.MachineName,
                Environment.OSVersion.VersionString,
                Convert.ToBase64String(serverNonce));
            await messages.WriteAsync(
                MessageType.ServerHello,
                PayloadJson.Serialize(serverHello),
                hostCancellationToken).ConfigureAwait(false);

            string pairingCode = PairingCode.Compute(_certificate!, clientNonce, serverNonce);
            PairingRequest request = new(remoteEndpoint, hello.DeviceName, hello.OperatingSystem, pairingCode);
            bool accepted = false;
            if (PairingApprovalHandler is not null)
            {
                using CancellationTokenSource approvalTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
                approvalTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                accepted = await PairingApprovalHandler(request, approvalTimeout.Token).ConfigureAwait(false);
            }

            SessionDecision decision = accepted
                ? new SessionDecision(true, null)
                : new SessionDecision(false, "被控端未核准本次控制。");
            await messages.WriteAsync(
                MessageType.SessionDecision,
                PayloadJson.Serialize(decision),
                hostCancellationToken).ConfigureAwait(false);
            if (!accepted)
            {
                OnStatus("已拒絕控制要求。");
                return;
            }

            using CancellationTokenSource sessionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
            await using IAsyncEnumerator<VideoFramePayload> frames =
                _screenFrameSource.CaptureAsync(sessionCancellation.Token).GetAsyncEnumerator(sessionCancellation.Token);
            if (!await frames.MoveNextAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException("畫面擷取沒有產生任何 frame。");
            }

            VideoFramePayload firstFrame = frames.Current;
            SessionReady ready = new(Guid.NewGuid(), firstFrame.Width, firstFrame.Height, firstFrame.Codec);
            await messages.WriteAsync(
                MessageType.SessionReady,
                PayloadJson.Serialize(ready),
                hostCancellationToken).ConfigureAwait(false);
            OnStatus($"已允許 {hello.DeviceName} 控制；配對碼 {pairingCode}");

            Task reader = ReadClientMessagesAsync(messages, sessionCancellation.Token);
            Task sender = SendFramesAsync(messages, frames, firstFrame, sessionCancellation.Token);
            await Task.WhenAny(reader, sender).ConfigureAwait(false);
            sessionCancellation.Cancel();
            await IgnoreExpectedSessionEndAsync(reader, sender).ConfigureAwait(false);
            OnStatus("控制 session 已結束，等待下一次連線。");
        }
    }

    private async Task ReadClientMessagesAsync(
        FramedMessageStream messages,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ProtocolPacket packet = await messages.ReadAsync(cancellationToken).ConfigureAwait(false);
            switch (packet.Type)
            {
                case MessageType.InputEvent:
                    RemoteInputEvent inputEvent = RemoteInputEvent.Deserialize(packet.Payload);
                    await _inputInjector.InjectAsync(inputEvent, cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.Ping:
                    await messages.WriteAsync(MessageType.Pong, packet.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case MessageType.Disconnect:
                    return;
                default:
                    throw new ProtocolException($"Unexpected client message {packet.Type}.");
            }
        }
    }

    private async Task SendFramesAsync(
        FramedMessageStream messages,
        IAsyncEnumerator<VideoFramePayload> frames,
        VideoFramePayload firstFrame,
        CancellationToken cancellationToken)
    {
        long captured = 0;
        long sent = 0;
        long dropped = 0;
        Stopwatch reportTimer = Stopwatch.StartNew();
        VideoFramePayload current = firstFrame;

        while (!cancellationToken.IsCancellationRequested)
        {
            captured++;
            Stopwatch sendTimer = Stopwatch.StartNew();
            await messages.WriteAsync(MessageType.VideoFrame, current.Serialize(), cancellationToken)
                .ConfigureAwait(false);
            sendTimer.Stop();
            sent++;

            if (reportTimer.Elapsed >= TimeSpan.FromSeconds(1))
            {
                MetricsPayload metrics = new(
                    captured,
                    sent,
                    dropped,
                    sendTimer.Elapsed.TotalMilliseconds,
                    current.Data.Length);
                MetricsChanged?.Invoke(metrics);
                await messages.WriteAsync(
                    MessageType.Metrics,
                    PayloadJson.Serialize(metrics),
                    cancellationToken).ConfigureAwait(false);
                reportTimer.Restart();
            }

            if (!await frames.MoveNextAsync().ConfigureAwait(false))
            {
                return;
            }

            current = frames.Current;
        }
    }

    private static byte[] DecodeNonce(string value)
    {
        byte[] nonce;
        try
        {
            nonce = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ProtocolException($"Nonce is not valid Base64: {exception.Message}");
        }

        if (nonce.Length != ProtocolConstants.NonceLength)
        {
            throw new ProtocolException("Nonce length is invalid.");
        }

        return nonce;
    }

    private static async Task IgnoreExpectedSessionEndAsync(params Task[] tasks)
    {
        foreach (Task task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }
    }

    public async Task StopAsync()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _lifetime.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        OnStatus("被控端已停止。");
    }

    private void OnStatus(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _certificate?.Dispose();
        await _screenFrameSource.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
