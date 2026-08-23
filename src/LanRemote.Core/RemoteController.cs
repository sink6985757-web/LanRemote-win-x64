using System.Collections.Concurrent;
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
    private readonly FileTransferReceiver _downloadReceiver;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DirectoryBrowseResponse>> _browseRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DropTargetResponse>> _dropTargetRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ClipboardFilesResponse>> _clipboardRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FileTransferDecision>> _uploadDecisions = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FileTransferResult>> _uploadResults = [];
    private readonly ConcurrentDictionary<Guid, PendingDownload> _downloadRequests = [];
    private readonly ConcurrentDictionary<Guid, PendingDownload> _downloadsByTransfer = [];
    private TcpClient? _client;
    private FramedMessageStream? _messages;
    private Task? _reader;
    private bool _ready;
    private QualityProfile _currentQualityProfile = QualityProfiles.Balanced;

    public RemoteController(FileTransferReceiver? downloadReceiver = null)
    {
        _downloadReceiver = downloadReceiver ?? new FileTransferReceiver();
    }

    public event Action<string>? StatusChanged;

    public event Action<string>? PairingCodeAvailable;

    public event Action<SessionReady>? SessionReadyReceived;

    public event Action<VideoFramePayload>? VideoFrameReceived;

    public event Action<MetricsPayload>? MetricsReceived;

    public event Action<QualityProfile>? QualityProfileAppliedReceived;

    public event Action<SecureAttentionResult>? SecureAttentionResultReceived;

    public event Action<FileTransferProgress>? FileTransferProgressChanged;

    public event Action<FileTransferResult>? FileTransferCompleted;

    public bool IsConnected => _ready && _client?.Connected == true;

    public QualityProfile CurrentQualityProfile => _currentQualityProfile;

    public bool FileTransferAllowed { get; private set; }

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

            FileTransferAllowed = decision.FileTransferAllowed;

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

    public async Task<DirectoryBrowseResponse> BrowseRemoteAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        Guid requestId = Guid.NewGuid();
        TaskCompletionSource<DirectoryBrowseResponse> completion = NewCompletion<DirectoryBrowseResponse>();
        if (!_browseRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("無法建立遠端目錄瀏覽要求。");
        }

        try
        {
            await _messages!.WriteAsync(
                MessageType.DirectoryBrowseRequest,
                PayloadJson.Serialize(new DirectoryBrowseRequest(requestId, path)),
                cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _browseRequests.TryRemove(requestId, out _);
        }
    }

    public async Task<DropTargetResponse> ResolveDropTargetAsync(
        float normalizedX,
        float normalizedY,
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        Guid requestId = Guid.NewGuid();
        TaskCompletionSource<DropTargetResponse> completion = NewCompletion<DropTargetResponse>();
        if (!_dropTargetRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("無法建立拖放目的地要求。");
        }

        try
        {
            await _messages!.WriteAsync(
                MessageType.DropTargetRequest,
                PayloadJson.Serialize(new DropTargetRequest(requestId, normalizedX, normalizedY)),
                cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dropTargetRequests.TryRemove(requestId, out _);
        }
    }

    public async Task<IReadOnlyList<string>> GetRemoteClipboardFilesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        Guid requestId = Guid.NewGuid();
        TaskCompletionSource<ClipboardFilesResponse> completion = NewCompletion<ClipboardFilesResponse>();
        if (!_clipboardRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("無法建立遠端剪貼簿要求。");
        }

        try
        {
            await _messages!.WriteAsync(
                MessageType.ClipboardFilesRequest,
                PayloadJson.Serialize(new ClipboardFilesRequest(requestId)),
                cancellationToken).ConfigureAwait(false);
            ClipboardFilesResponse response =
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                throw new InvalidOperationException(response.Error);
            }

            return response.Paths;
        }
        finally
        {
            _clipboardRequests.TryRemove(requestId, out _);
        }
    }

    public async Task<FileTransferResult> UploadAsync(
        IReadOnlyList<string> sourcePaths,
        string remoteDestination,
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        Guid transferSeed = FileTransferIdentity.CreateSeed("upload", remoteDestination, sourcePaths);
        PreparedFileTransfer transfer = await FileTransferManifestBuilder.CreateAsync(
            sourcePaths,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        transfer = transfer with
        {
            TransferId = FileTransferIdentity.WithManifest(transferSeed, transfer.ManifestSha256),
        };
        TaskCompletionSource<FileTransferDecision> decisionCompletion = NewCompletion<FileTransferDecision>();
        TaskCompletionSource<FileTransferResult> resultCompletion = NewCompletion<FileTransferResult>();
        if (!_uploadDecisions.TryAdd(transfer.TransferId, decisionCompletion) ||
            !_uploadResults.TryAdd(transfer.TransferId, resultCompletion))
        {
            _uploadDecisions.TryRemove(transfer.TransferId, out _);
            _uploadResults.TryRemove(transfer.TransferId, out _);
            throw new InvalidOperationException("相同的檔案傳輸已在進行中。");
        }

        bool completed = false;
        try
        {
            FileUploadOffer offer = new(
                transfer.TransferId,
                remoteDestination,
                transfer.Entries,
                transfer.TotalBytes,
                transfer.ManifestSha256);
            await _messages!.WriteAsync(
                MessageType.FileUploadOffer,
                PayloadJson.Serialize(offer),
                cancellationToken).ConfigureAwait(false);
            FileTransferDecision decision =
                await decisionCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!decision.Accepted)
            {
                return new FileTransferResult(
                    transfer.TransferId,
                    false,
                    decision.Reason ?? "被控端拒絕檔案傳輸。",
                    []);
            }

            await FileTransferSender.SendAsync(
                transfer,
                decision.ResumePoints,
                _messages,
                MessageType.FileUploadChunk,
                FileTransferDirection.Upload,
                progress => FileTransferProgressChanged?.Invoke(progress),
                cancellationToken).ConfigureAwait(false);
            await _messages.WriteAsync(
                MessageType.FileUploadComplete,
                PayloadJson.Serialize(new FileTransferComplete(transfer.TransferId)),
                cancellationToken).ConfigureAwait(false);
            FileTransferResult result =
                await resultCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            completed = result.Succeeded;
            return result;
        }
        finally
        {
            _uploadDecisions.TryRemove(transfer.TransferId, out _);
            _uploadResults.TryRemove(transfer.TransferId, out _);
            if (!completed && cancellationToken.IsCancellationRequested && _messages is not null)
            {
                await TrySendCancelAsync(
                    transfer.TransferId,
                    FileTransferDirection.Upload,
                    deletePartialFiles: false).ConfigureAwait(false);
            }
        }
    }

    public async Task<FileTransferResult> DownloadAsync(
        IReadOnlyList<string> remoteSourcePaths,
        string localDestination,
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        _ = FileTransferPolicy.ValidateExistingDirectory(localDestination);
        Guid requestId = Guid.NewGuid();
        Guid transferId = FileTransferIdentity.CreateSeed("download", localDestination, remoteSourcePaths);
        PendingDownload pending = new(localDestination);
        if (!_downloadRequests.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("無法建立下載要求。");
        }

        try
        {
            await _messages!.WriteAsync(
                MessageType.FileDownloadRequest,
                PayloadJson.Serialize(new FileDownloadRequest(requestId, transferId, remoteSourcePaths)),
                cancellationToken).ConfigureAwait(false);
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _downloadRequests.TryRemove(requestId, out _);
            if (pending.TransferId is Guid activeId)
            {
                _downloadsByTransfer.TryRemove(activeId, out _);
                if (cancellationToken.IsCancellationRequested)
                {
                    await TrySendCancelAsync(
                        activeId,
                        FileTransferDirection.Download,
                        deletePartialFiles: false).ConfigureAwait(false);
                }
            }
        }
    }

    public async Task CancelTransferAsync(
        Guid transferId,
        FileTransferDirection direction,
        bool deletePartialFiles,
        CancellationToken cancellationToken = default)
    {
        EnsureFileTransferAvailable();
        if (direction == FileTransferDirection.Download)
        {
            await _downloadReceiver.CancelAsync(transferId, deletePartialFiles, cancellationToken)
                .ConfigureAwait(false);
        }

        await _messages!.WriteAsync(
            MessageType.FileTransferCancel,
            PayloadJson.Serialize(new FileTransferCancel(transferId, direction, deletePartialFiles)),
            cancellationToken).ConfigureAwait(false);
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
                    case MessageType.DirectoryBrowseResponse:
                        DirectoryBrowseResponse browse =
                            PayloadJson.Deserialize<DirectoryBrowseResponse>(packet.Payload);
                        if (_browseRequests.TryGetValue(browse.RequestId, out TaskCompletionSource<DirectoryBrowseResponse>? browseCompletion))
                        {
                            browseCompletion.TrySetResult(browse);
                        }

                        break;
                    case MessageType.DropTargetResponse:
                        DropTargetResponse drop = PayloadJson.Deserialize<DropTargetResponse>(packet.Payload);
                        if (_dropTargetRequests.TryGetValue(drop.RequestId, out TaskCompletionSource<DropTargetResponse>? dropCompletion))
                        {
                            dropCompletion.TrySetResult(drop);
                        }

                        break;
                    case MessageType.ClipboardFilesResponse:
                        ClipboardFilesResponse clipboard =
                            PayloadJson.Deserialize<ClipboardFilesResponse>(packet.Payload);
                        if (_clipboardRequests.TryGetValue(clipboard.RequestId, out TaskCompletionSource<ClipboardFilesResponse>? clipboardCompletion))
                        {
                            clipboardCompletion.TrySetResult(clipboard);
                        }

                        break;
                    case MessageType.FileUploadDecision:
                        FileTransferDecision uploadDecision =
                            PayloadJson.Deserialize<FileTransferDecision>(packet.Payload);
                        if (_uploadDecisions.TryGetValue(uploadDecision.TransferId, out TaskCompletionSource<FileTransferDecision>? uploadDecisionCompletion))
                        {
                            uploadDecisionCompletion.TrySetResult(uploadDecision);
                        }

                        break;
                    case MessageType.FileUploadResult:
                        FileTransferResult uploadResult =
                            PayloadJson.Deserialize<FileTransferResult>(packet.Payload);
                        FileTransferCompleted?.Invoke(uploadResult);
                        if (_uploadResults.TryGetValue(uploadResult.TransferId, out TaskCompletionSource<FileTransferResult>? uploadResultCompletion))
                        {
                            uploadResultCompletion.TrySetResult(uploadResult);
                        }

                        break;
                    case MessageType.FileDownloadOffer:
                        await HandleDownloadOfferAsync(
                            PayloadJson.Deserialize<FileDownloadOffer>(packet.Payload),
                            cancellationToken).ConfigureAwait(false);
                        break;
                    case MessageType.FileDownloadChunk:
                        FileChunkPayload downloadChunk = FileChunkPayload.Deserialize(packet.Payload);
                        FileTransferProgress downloadProgress = await _downloadReceiver.ReceiveChunkAsync(
                            downloadChunk,
                            FileTransferDirection.Download,
                            cancellationToken).ConfigureAwait(false);
                        FileTransferProgressChanged?.Invoke(downloadProgress);
                        break;
                    case MessageType.FileDownloadComplete:
                        FileTransferComplete downloadComplete =
                            PayloadJson.Deserialize<FileTransferComplete>(packet.Payload);
                        _ = CompleteDownloadSafelyAsync(downloadComplete, cancellationToken);
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
            FileTransferAllowed = false;
            CancelPendingRequests();
            await _downloadReceiver.SuspendAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task HandleDownloadOfferAsync(
        FileDownloadOffer offer,
        CancellationToken cancellationToken)
    {
        if (!_downloadRequests.TryGetValue(offer.RequestId, out PendingDownload? pending))
        {
            await _messages!.WriteAsync(
                MessageType.FileDownloadDecision,
                PayloadJson.Serialize(new FileTransferDecision(
                    offer.TransferId,
                    false,
                    "下載要求已取消或不存在。",
                    [])),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(offer.Error))
        {
            pending.Completion.TrySetResult(new FileTransferResult(
                offer.TransferId,
                false,
                offer.Error,
                []));
            return;
        }

        pending.TransferId = offer.TransferId;
        _downloadsByTransfer[offer.TransferId] = pending;
        FileTransferDecision decision = await _downloadReceiver.AcceptDownloadAsync(
            offer,
            pending.DestinationPath,
            cancellationToken).ConfigureAwait(false);
        await _messages!.WriteAsync(
            MessageType.FileDownloadDecision,
            PayloadJson.Serialize(decision),
            cancellationToken).ConfigureAwait(false);
        if (!decision.Accepted)
        {
            pending.Completion.TrySetResult(new FileTransferResult(
                offer.TransferId,
                false,
                decision.Reason ?? "本機拒絕下載。",
                []));
        }
    }

    private async Task CompleteDownloadSafelyAsync(
        FileTransferComplete complete,
        CancellationToken cancellationToken)
    {
        FileTransferResult result;
        try
        {
            result = await _downloadReceiver.CompleteAsync(complete.TransferId, cancellationToken)
                .ConfigureAwait(false);
            if (_messages is not null)
            {
                await _messages.WriteAsync(
                    MessageType.FileDownloadResult,
                    PayloadJson.Serialize(result),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            result = new FileTransferResult(complete.TransferId, false, exception.Message, []);
        }

        FileTransferCompleted?.Invoke(result);
        if (_downloadsByTransfer.TryRemove(complete.TransferId, out PendingDownload? pendingDownload))
        {
            pendingDownload.Completion.TrySetResult(result);
        }
    }

    private async Task TrySendCancelAsync(
        Guid transferId,
        FileTransferDirection direction,
        bool deletePartialFiles)
    {
        try
        {
            if (_messages is not null && _ready)
            {
                await _messages.WriteAsync(
                    MessageType.FileTransferCancel,
                    PayloadJson.Serialize(new FileTransferCancel(
                        transferId,
                        direction,
                        deletePartialFiles))).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private void EnsureFileTransferAvailable()
    {
        if (!_ready || _messages is null)
        {
            throw new InvalidOperationException("請先建立控制 session。");
        }

        if (!FileTransferAllowed)
        {
            throw new UnauthorizedAccessException("被控端未允許本次 session 的檔案傳輸。");
        }
    }

    private void CancelPendingRequests()
    {
        Exception exception = new IOException("控制 session 已中斷。");
        foreach (TaskCompletionSource<DirectoryBrowseResponse> pending in _browseRequests.Values)
        {
            pending.TrySetException(exception);
        }

        foreach (TaskCompletionSource<DropTargetResponse> pending in _dropTargetRequests.Values)
        {
            pending.TrySetException(exception);
        }

        foreach (TaskCompletionSource<ClipboardFilesResponse> pending in _clipboardRequests.Values)
        {
            pending.TrySetException(exception);
        }

        foreach (TaskCompletionSource<FileTransferDecision> pending in _uploadDecisions.Values)
        {
            pending.TrySetException(exception);
        }

        foreach (TaskCompletionSource<FileTransferResult> pending in _uploadResults.Values)
        {
            pending.TrySetException(exception);
        }

        foreach (PendingDownload pending in _downloadRequests.Values)
        {
            pending.Completion.TrySetException(exception);
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
        FileTransferAllowed = false;
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

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _inputSequenceGate.Dispose();
        _lifetime.Dispose();
    }

    private sealed class PendingDownload(string destinationPath)
    {
        public string DestinationPath { get; } = destinationPath;

        public Guid? TransferId { get; set; }

        public TaskCompletionSource<FileTransferResult> Completion { get; } =
            NewCompletion<FileTransferResult>();
    }
}
