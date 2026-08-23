using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LanRemote.Protocol;

namespace LanRemote.Core;

public sealed class RemoteController : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _inputSequenceGate = new(1, 1);
    private TcpClient? _client;
    private FramedMessageStream? _messages;
    private Task? _reader;
    private bool _ready;
    private QualityProfile _currentQualityProfile = QualityProfiles.Balanced;

    public event Action<string>? StatusChanged;

    public event Action<string>? PairingCodeAvailable;

    public event Action<SessionReady>? SessionReadyReceived;

    public event Action<VideoFramePayload>? VideoFrameReceived;

    public event Action<MetricsPayload>? MetricsReceived;

    public event Action<QualityProfile>? QualityProfileAppliedReceived;

    public event Action<SecureAttentionResult>? SecureAttentionResultReceived;

    public bool IsConnected => _ready && _client?.Connected == true;

    public QualityProfile CurrentQualityProfile => _currentQualityProfile;

    public Task ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken = default) =>
        ConnectAsync(endpoint, QualityPreset.Balanced, cancellationToken);

    public async Task ConnectAsync(
        IPEndPoint endpoint,
        QualityPreset initialQualityPreset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (_client is not null)
        {
            throw new InvalidOperationException("Controller is already connected.");
        }

        if (!LanAddressPolicy.IsAllowed(endpoint.Address))
        {
            throw new InvalidOperationException("Only private LAN or loopback addresses are allowed.");
        }

        _currentQualityProfile = QualityProfiles.Get(initialQualityPreset);

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        _client = new TcpClient(AddressFamily.InterNetwork)
        {
            NoDelay = true,
        };
        await _client.ConnectAsync(endpoint, linked.Token).ConfigureAwait(false);
        OnStatus($"已連線至 {endpoint}，正在建立加密 session。");

        X509Certificate2? serverCertificate = null;
        SslStream sslStream = new(
            _client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                serverCertificate?.Dispose();
                serverCertificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
                return true;
            });

        try
        {
            SslClientAuthenticationOptions authenticationOptions = new()
            {
                TargetHost = "LanRemote-Ephemeral",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };
            await sslStream.AuthenticateAsClientAsync(authenticationOptions, linked.Token).ConfigureAwait(false);
            if (serverCertificate is null)
            {
                throw new AuthenticationException("Remote host did not provide a certificate.");
            }

            _messages = new FramedMessageStream(sslStream);
            byte[] clientNonce = RandomNumberGenerator.GetBytes(ProtocolConstants.NonceLength);
            ClientHello hello = new(
                ProtocolConstants.Version,
                Environment.MachineName,
                Environment.OSVersion.VersionString,
                Convert.ToBase64String(clientNonce),
                initialQualityPreset);
            await _messages.WriteAsync(
                MessageType.ClientHello,
                PayloadJson.Serialize(hello),
                linked.Token).ConfigureAwait(false);

            ProtocolPacket serverPacket = await _messages.ReadAsync(linked.Token).ConfigureAwait(false);
            if (serverPacket.Type != MessageType.ServerHello)
            {
                throw new ProtocolException("Remote host did not send ServerHello.");
            }

            ServerHello serverHello = PayloadJson.Deserialize<ServerHello>(serverPacket.Payload);
            if (serverHello.ProtocolVersion != ProtocolConstants.Version)
            {
                throw new ProtocolException(
                    $"協定版本不相容：控制端需要 v{ProtocolConstants.Version}，被控端送出 v{serverHello.ProtocolVersion}。");
            }

            byte[] serverNonce = DecodeNonce(serverHello.NonceBase64);
            string pairingCode = PairingCode.Compute(serverCertificate, clientNonce, serverNonce);
            PairingCodeAvailable?.Invoke(pairingCode);
            OnStatus($"請核對被控端顯示的配對碼：{pairingCode}");

            ProtocolPacket decisionPacket = await _messages.ReadAsync(linked.Token).ConfigureAwait(false);
            if (decisionPacket.Type != MessageType.SessionDecision)
            {
                throw new ProtocolException("Remote host did not send a session decision.");
            }

            SessionDecision decision = PayloadJson.Deserialize<SessionDecision>(decisionPacket.Payload);
            if (!decision.Accepted)
            {
                throw new UnauthorizedAccessException(decision.Reason ?? "被控端拒絕控制要求。");
            }

            ProtocolPacket readyPacket = await _messages.ReadAsync(linked.Token).ConfigureAwait(false);
            if (readyPacket.Type != MessageType.SessionReady)
            {
                throw new ProtocolException("Remote host did not initialize the screen session.");
            }

            SessionReady ready = PayloadJson.Deserialize<SessionReady>(readyPacket.Payload);
            if (!QualityProfiles.IsCanonical(ready.QualityProfile))
            {
                throw new ProtocolException("Remote host returned a non-canonical quality profile.");
            }

            _currentQualityProfile = ready.QualityProfile;
            _ready = true;
            SessionReadyReceived?.Invoke(ready);
            QualityProfileAppliedReceived?.Invoke(ready.QualityProfile);
            OnStatus($"控制 session 已啟用：{ready.Width}×{ready.Height} {ready.Codec} / " +
                     ready.QualityProfile.DisplayName);
            _reader = ReadServerMessagesAsync(_lifetime.Token);
        }
        catch (Exception exception)
        {
            serverCertificate?.Dispose();
            await sslStream.DisposeAsync().ConfigureAwait(false);
            _client.Dispose();
            _client = null;
            _messages = null;
            if (exception is IOException)
            {
                throw new ProtocolException(
                    $"連線在協定交握時中斷；請確認兩臺都使用 LanRemote v{ProtocolConstants.Version}。",
                    exception);
            }

            throw;
        }
        finally
        {
            serverCertificate?.Dispose();
        }
    }

    public async ValueTask SendInputAsync(
        RemoteInputEvent inputEvent,
        CancellationToken cancellationToken = default)
    {
        await _inputSequenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteInputCoreAsync(inputEvent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inputSequenceGate.Release();
        }
    }

    public async ValueTask SendShortcutAsync(
        RemoteShortcut shortcut,
        CancellationToken cancellationToken = default)
    {
        if (!RemoteShortcut.TryValidate(shortcut, out string error))
        {
            throw new ArgumentException(error, nameof(shortcut));
        }

        await _inputSequenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (RemoteInputEvent inputEvent in shortcut.ToInputEvents())
            {
                await WriteInputCoreAsync(inputEvent, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _inputSequenceGate.Release();
        }
    }

    private async ValueTask WriteInputCoreAsync(
        RemoteInputEvent inputEvent,
        CancellationToken cancellationToken)
    {
        if (!_ready || _messages is null)
        {
            return;
        }

        await _messages.WriteAsync(
            MessageType.InputEvent,
            inputEvent.Serialize(),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ChangeQualityProfileAsync(
        QualityPreset preset,
        CancellationToken cancellationToken = default)
    {
        QualityProfile requested = QualityProfiles.Get(preset);
        _currentQualityProfile = requested;
        if (!_ready || _messages is null)
        {
            return;
        }

        await _messages.WriteAsync(
            MessageType.QualityProfileRequest,
            PayloadJson.Serialize(new QualityProfileRequest(preset)),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<Guid?> RequestSecureAttentionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_ready || _messages is null)
        {
            return null;
        }

        Guid requestId = Guid.NewGuid();
        await _messages.WriteAsync(
            MessageType.SecureAttentionRequest,
            PayloadJson.Serialize(new SecureAttentionRequest(requestId)),
            cancellationToken).ConfigureAwait(false);
        return requestId;
    }

    private async Task ReadServerMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _messages is not null)
            {
                ProtocolPacket packet = await _messages.ReadAsync(cancellationToken).ConfigureAwait(false);
                switch (packet.Type)
                {
                    case MessageType.VideoFrame:
                        VideoFrameReceived?.Invoke(VideoFramePayload.Deserialize(packet.Payload));
                        break;
                    case MessageType.Metrics:
                        MetricsReceived?.Invoke(PayloadJson.Deserialize<MetricsPayload>(packet.Payload));
                        break;
                    case MessageType.QualityProfileApplied:
                        QualityProfile applied = PayloadJson.Deserialize<QualityProfile>(packet.Payload);
                        if (!QualityProfiles.IsCanonical(applied))
                        {
                            throw new ProtocolException("Remote host returned a non-canonical quality profile.");
                        }

                        _currentQualityProfile = applied;
                        QualityProfileAppliedReceived?.Invoke(applied);
                        OnStatus($"畫面模式已切換為 {applied.DisplayName}。");
                        break;
                    case MessageType.SecureAttentionResult:
                        SecureAttentionResult result =
                            PayloadJson.Deserialize<SecureAttentionResult>(packet.Payload);
                        SecureAttentionResultReceived?.Invoke(result);
                        OnStatus(result.Succeeded
                            ? "被控端已接受 Ctrl+Alt+Delete 要求。"
                            : result.Message);
                        break;
                    case MessageType.Ping:
                        await _messages.WriteAsync(MessageType.Pong, packet.Payload, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case MessageType.Disconnect:
                        OnStatus("被控端已中止 session。");
                        return;
                    case MessageType.Error:
                        ErrorPayload error = PayloadJson.Deserialize<ErrorPayload>(packet.Payload);
                        OnStatus($"被控端錯誤：{error.Message}");
                        return;
                    default:
                        throw new ProtocolException($"Unexpected server message {packet.Type}.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException exception)
        {
            OnStatus($"連線已中斷：{exception.Message}");
        }
        finally
        {
            _ready = false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_messages is not null && _ready)
        {
            try
            {
                await _messages.WriteAsync(MessageType.Disconnect, ReadOnlyMemory<byte>.Empty)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        _ready = false;
        _lifetime.Cancel();
        if (_reader is not null)
        {
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_messages is not null)
        {
            await _messages.DisposeAsync().ConfigureAwait(false);
            _messages = null;
        }

        _client?.Dispose();
        _client = null;
        OnStatus("控制端已斷線。");
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

    private void OnStatus(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _inputSequenceGate.Dispose();
        _lifetime.Dispose();
    }
}
