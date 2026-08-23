using System.Diagnostics;
using System.Collections.Concurrent;
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
    private readonly ISecureAttentionProvider _secureAttentionProvider;
    private readonly IRemoteDropTargetResolver _dropTargetResolver;
    private readonly IClipboardFileProvider _clipboardFileProvider;
    private readonly FileTransferReceiver _fileReceiver;
    private readonly ConcurrentDictionary<Guid, PreparedFileTransfer> _outgoingDownloads = [];
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _outgoingDownloadCancellations = [];
    private readonly CancellationTokenSource _lifetime = new();
    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private Task? _acceptLoop;

    public RemoteHost(
        IScreenFrameSource screenFrameSource,
        IInputInjector inputInjector,
        ISecureAttentionProvider? secureAttentionProvider = null,
        IRemoteDropTargetResolver? dropTargetResolver = null,
        IClipboardFileProvider? clipboardFileProvider = null,
        FileTransferReceiver? fileReceiver = null)
    {
        _screenFrameSource = screenFrameSource ?? throw new ArgumentNullException(nameof(screenFrameSource));
        _inputInjector = inputInjector ?? throw new ArgumentNullException(nameof(inputInjector));
        _secureAttentionProvider = secureAttentionProvider ?? new UnavailableSecureAttentionProvider();
        _dropTargetResolver = dropTargetResolver ?? new UnavailableRemoteDropTargetResolver();
        _clipboardFileProvider = clipboardFileProvider ?? new EmptyClipboardFileProvider();
        _fileReceiver = fileReceiver ?? new FileTransferReceiver();
    }

    public Func<PairingRequest, CancellationToken, Task<PairingApproval>>? PairingApprovalHandler { get; set; }

    public event Action<string>? StatusChanged;

    public event Action<MetricsPayload>? MetricsChanged;

    public event Action<FileTransferProgress>? FileTransferProgressChanged;

    public event Action<FileTransferResult>? FileTransferCompleted;

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
                throw new ProtocolException(
                    $"協定版本不相容：被控端需要 v{ProtocolConstants.Version}，控制端送出 v{hello.ProtocolVersion}。");
            }

            QualityProfile initialProfile = QualityProfiles.Get(hello.InitialQualityPreset);
            _screenFrameSource.ApplyQualityProfile(initialProfile);

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
            PairingApproval approval = new(false, false);
            if (PairingApprovalHandler is not null)
            {
                using CancellationTokenSource approvalTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
                approvalTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                approval = await PairingApprovalHandler(request, approvalTimeout.Token).ConfigureAwait(false);
            }

            SessionDecision decision = approval.Accepted
                ? new SessionDecision(true, null, approval.FileTransferAllowed)
                : new SessionDecision(false, "被控端未核准本次控制。", false);
            await messages.WriteAsync(
                MessageType.SessionDecision,
                PayloadJson.Serialize(decision),
                hostCancellationToken).ConfigureAwait(false);
            if (!approval.Accepted)
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
            SessionReady ready = new(
                Guid.NewGuid(),
                firstFrame.Width,
                firstFrame.Height,
                firstFrame.Codec,
                _screenFrameSource.CurrentQualityProfile);
            await messages.WriteAsync(
                MessageType.SessionReady,
                PayloadJson.Serialize(ready),
                hostCancellationToken).ConfigureAwait(false);
            OnStatus($"已允許 {hello.DeviceName} 控制；配對碼 {pairingCode}");

            Task reader = ReadClientMessagesAsync(
                messages,
                approval.FileTransferAllowed,
                sessionCancellation.Token);
            Task sender = SendFramesAsync(messages, frames, firstFrame, sessionCancellation.Token);
            await Task.WhenAny(reader, sender).ConfigureAwait(false);
            sessionCancellation.Cancel();
            CancelOutgoingDownloads();
            await IgnoreExpectedSessionEndAsync(reader, sender).ConfigureAwait(false);
            await _fileReceiver.SuspendAllAsync(CancellationToken.None).ConfigureAwait(false);
            OnStatus("控制 session 已結束，等待下一次連線。");
        }
    }

    private async Task ReadClientMessagesAsync(
        FramedMessageStream messages,
        bool fileTransferAllowed,
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
                case MessageType.QualityProfileRequest:
                    QualityProfileRequest request =
                        PayloadJson.Deserialize<QualityProfileRequest>(packet.Payload);
                    QualityProfile profile = QualityProfiles.Get(request.Preset);
                    _screenFrameSource.ApplyQualityProfile(profile);
                    await messages.WriteAsync(
                        MessageType.QualityProfileApplied,
                        PayloadJson.Serialize(profile),
                        cancellationToken).ConfigureAwait(false);
                    OnStatus($"畫面模式已切換為 {profile.DisplayName}：" +
                             $"{profile.MaximumWidth}×{profile.MaximumHeight} / " +
                             $"{profile.FramesPerSecond} fps");
                    break;
                case MessageType.SecureAttentionRequest:
                    SecureAttentionRequest secureAttentionRequest =
                        PayloadJson.Deserialize<SecureAttentionRequest>(packet.Payload);
                    SecureAttentionResult secureAttentionResult =
                        await _secureAttentionProvider.RequestAsync(secureAttentionRequest, cancellationToken)
                            .ConfigureAwait(false);
                    await messages.WriteAsync(
                        MessageType.SecureAttentionResult,
                        PayloadJson.Serialize(secureAttentionResult),
                        cancellationToken).ConfigureAwait(false);
                    OnStatus(secureAttentionResult.Succeeded
                        ? "已送出 Ctrl+Alt+Delete。"
                        : secureAttentionResult.Message);
                    break;
                case MessageType.DirectoryBrowseRequest:
                    DirectoryBrowseRequest browseRequest =
                        PayloadJson.Deserialize<DirectoryBrowseRequest>(packet.Payload);
                    DirectoryBrowseResponse browseResponse = fileTransferAllowed
                        ? RemoteFileBrowser.Browse(browseRequest)
                        : new DirectoryBrowseResponse(
                            browseRequest.RequestId,
                            browseRequest.Path ?? string.Empty,
                            [],
                            false,
                            "被控端未允許本次 session 的檔案傳輸。");
                    await messages.WriteAsync(
                        MessageType.DirectoryBrowseResponse,
                        PayloadJson.Serialize(browseResponse),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.DropTargetRequest:
                    DropTargetRequest dropRequest = PayloadJson.Deserialize<DropTargetRequest>(packet.Payload);
                    DropTargetResponse dropResponse;
                    if (!fileTransferAllowed)
                    {
                        dropResponse = new DropTargetResponse(
                            dropRequest.RequestId,
                            null,
                            "被控端未允許檔案傳輸。");
                    }
                    else
                    {
                        try
                        {
                            string? path = await _dropTargetResolver.ResolveAsync(
                                dropRequest.NormalizedX,
                                dropRequest.NormalizedY,
                                cancellationToken).ConfigureAwait(false);
                            dropResponse = new DropTargetResponse(
                                dropRequest.RequestId,
                                path,
                                path is null ? "無法可靠辨識遠端目的資料夾。" : null);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            dropResponse = new DropTargetResponse(dropRequest.RequestId, null, exception.Message);
                        }
                    }

                    await messages.WriteAsync(
                        MessageType.DropTargetResponse,
                        PayloadJson.Serialize(dropResponse),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.ClipboardFilesRequest:
                    ClipboardFilesRequest clipboardRequest =
                        PayloadJson.Deserialize<ClipboardFilesRequest>(packet.Payload);
                    ClipboardFilesResponse clipboardResponse;
                    if (!fileTransferAllowed)
                    {
                        clipboardResponse = new ClipboardFilesResponse(
                            clipboardRequest.RequestId,
                            [],
                            "被控端未允許檔案傳輸。");
                    }
                    else
                    {
                        try
                        {
                            IReadOnlyList<string> paths = await _clipboardFileProvider
                                .GetFilesAsync(cancellationToken)
                                .ConfigureAwait(false);
                            clipboardResponse = new ClipboardFilesResponse(
                                clipboardRequest.RequestId,
                                paths.Where(FileTransferPolicy.IsTransferablePath).Take(200).ToArray(),
                                null);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            clipboardResponse = new ClipboardFilesResponse(
                                clipboardRequest.RequestId,
                                [],
                                exception.Message);
                        }
                    }

                    await messages.WriteAsync(
                        MessageType.ClipboardFilesResponse,
                        PayloadJson.Serialize(clipboardResponse),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.FileUploadOffer:
                    FileUploadOffer uploadOffer = PayloadJson.Deserialize<FileUploadOffer>(packet.Payload);
                    FileTransferDecision uploadDecision = fileTransferAllowed
                        ? await _fileReceiver.AcceptUploadAsync(uploadOffer, cancellationToken).ConfigureAwait(false)
                        : new FileTransferDecision(
                            uploadOffer.TransferId,
                            false,
                            "被控端未允許本次 session 的檔案傳輸。",
                            []);
                    await messages.WriteAsync(
                        MessageType.FileUploadDecision,
                        PayloadJson.Serialize(uploadDecision),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.FileUploadChunk:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferProgress uploadProgress = await _fileReceiver.ReceiveChunkAsync(
                        FileChunkPayload.Deserialize(packet.Payload),
                        FileTransferDirection.Upload,
                        cancellationToken).ConfigureAwait(false);
                    FileTransferProgressChanged?.Invoke(uploadProgress);
                    break;
                case MessageType.FileUploadComplete:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferComplete uploadComplete =
                        PayloadJson.Deserialize<FileTransferComplete>(packet.Payload);
                    _ = CompleteUploadSafelyAsync(messages, uploadComplete, cancellationToken);
                    break;
                case MessageType.FileDownloadRequest:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileDownloadRequest downloadRequest =
                        PayloadJson.Deserialize<FileDownloadRequest>(packet.Payload);
                    _ = PrepareDownloadSafelyAsync(messages, downloadRequest, cancellationToken);
                    break;
                case MessageType.FileDownloadDecision:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferDecision downloadDecision =
                        PayloadJson.Deserialize<FileTransferDecision>(packet.Payload);
                    StartDownload(messages, downloadDecision, cancellationToken);
                    break;
                case MessageType.FileDownloadResult:
                    FileTransferResult downloadResult =
                        PayloadJson.Deserialize<FileTransferResult>(packet.Payload);
                    _outgoingDownloads.TryRemove(downloadResult.TransferId, out _);
                    if (_outgoingDownloadCancellations.TryRemove(downloadResult.TransferId, out CancellationTokenSource? sentCts))
                    {
                        sentCts.Dispose();
                    }

                    OnStatus(downloadResult.Message);
                    FileTransferCompleted?.Invoke(downloadResult);
                    break;
                case MessageType.FileTransferCancel:
                    FileTransferCancel cancel = PayloadJson.Deserialize<FileTransferCancel>(packet.Payload);
                    if (cancel.Direction == FileTransferDirection.Upload)
                    {
                        await _fileReceiver.CancelAsync(
                            cancel.TransferId,
                            cancel.DeletePartialFiles,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (_outgoingDownloadCancellations.TryRemove(
                                 cancel.TransferId,
                                 out CancellationTokenSource? downloadCts))
                    {
                        downloadCts.Cancel();
                        downloadCts.Dispose();
                        _outgoingDownloads.TryRemove(cancel.TransferId, out _);
                    }

                    break;
                case MessageType.Disconnect:
                    return;
                default:
                    throw new ProtocolException($"Unexpected client message {packet.Type}.");
            }
        }
    }

    private async Task PrepareDownloadAsync(
        FramedMessageStream messages,
        FileDownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || request.TransferId == Guid.Empty ||
            request.SourcePaths.Count is 0 or > 200)
        {
            throw new InvalidDataException("遠端下載要求的識別碼或來源數量無效。");
        }

        PreparedFileTransfer transfer = await FileTransferManifestBuilder.CreateAsync(
            request.SourcePaths,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        transfer = transfer with
        {
            TransferId = FileTransferIdentity.WithManifest(request.TransferId, transfer.ManifestSha256),
        };
        if (!_outgoingDownloads.TryAdd(transfer.TransferId, transfer))
        {
            throw new InvalidOperationException("無法登記遠端下載。");
        }

        FileDownloadOffer offer = new(
            request.RequestId,
            transfer.TransferId,
            transfer.Entries,
            transfer.TotalBytes,
            transfer.ManifestSha256,
            null);
        await messages.WriteAsync(
            MessageType.FileDownloadOffer,
            PayloadJson.Serialize(offer),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteUploadSafelyAsync(
        FramedMessageStream messages,
        FileTransferComplete complete,
        CancellationToken cancellationToken)
    {
        try
        {
            FileTransferResult result = await _fileReceiver
                .CompleteAsync(complete.TransferId, cancellationToken)
                .ConfigureAwait(false);
            await messages.WriteAsync(
                MessageType.FileUploadResult,
                PayloadJson.Serialize(result),
                cancellationToken).ConfigureAwait(false);
            OnStatus(result.Message);
            FileTransferCompleted?.Invoke(result);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
            OnStatus($"接收檔案完成處理中斷：{exception.Message}");
        }
    }

    private async Task PrepareDownloadSafelyAsync(
        FramedMessageStream messages,
        FileDownloadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await PrepareDownloadAsync(messages, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            try
            {
                FileDownloadOffer rejected = new(
                    request.RequestId,
                    request.TransferId,
                    [],
                    0,
                    string.Empty,
                    exception.Message);
                await messages.WriteAsync(
                    MessageType.FileDownloadOffer,
                    PayloadJson.Serialize(rejected),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sendException) when (sendException is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private void StartDownload(
        FramedMessageStream messages,
        FileTransferDecision decision,
        CancellationToken sessionCancellationToken)
    {
        if (!_outgoingDownloads.TryGetValue(decision.TransferId, out PreparedFileTransfer? transfer))
        {
            throw new InvalidDataException("下載決定引用未知的傳輸。");
        }

        if (!decision.Accepted)
        {
            _outgoingDownloads.TryRemove(decision.TransferId, out _);
            OnStatus(decision.Reason ?? "控制端拒絕接收下載。");
            return;
        }

        CancellationTokenSource transferCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(sessionCancellationToken);
        if (!_outgoingDownloadCancellations.TryAdd(decision.TransferId, transferCancellation))
        {
            transferCancellation.Dispose();
            throw new InvalidOperationException("下載傳輸已經開始。");
        }

        _ = SendDownloadAsync(messages, transfer, decision.ResumePoints, transferCancellation.Token);
    }

    private async Task SendDownloadAsync(
        FramedMessageStream messages,
        PreparedFileTransfer transfer,
        IReadOnlyList<FileResumePoint> resumePoints,
        CancellationToken cancellationToken)
    {
        try
        {
            await FileTransferSender.SendAsync(
                transfer,
                resumePoints,
                messages,
                MessageType.FileDownloadChunk,
                FileTransferDirection.Download,
                progress => FileTransferProgressChanged?.Invoke(progress),
                cancellationToken).ConfigureAwait(false);
            await messages.WriteAsync(
                MessageType.FileDownloadComplete,
                PayloadJson.Serialize(new FileTransferComplete(transfer.TransferId)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException)
        {
            OnStatus($"下載傳送失敗：{exception.Message}");
        }
    }

    private static void EnsureFileTransferAllowed(bool allowed)
    {
        if (!allowed)
        {
            throw new UnauthorizedAccessException("被控端未允許本次 session 的檔案傳輸。");
        }
    }

    private void CancelOutgoingDownloads()
    {
        foreach ((Guid _, CancellationTokenSource cancellation) in _outgoingDownloadCancellations)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _outgoingDownloadCancellations.Clear();
        _outgoingDownloads.Clear();
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
