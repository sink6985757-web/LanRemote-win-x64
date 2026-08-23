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

public sealed class RemoteHost : IFileTransferSession, IAsyncDisposable
{
    private readonly IScreenFrameSource _screenFrameSource;
    private readonly IInputInjector _inputInjector;
    private readonly ISecureAttentionProvider _secureAttentionProvider;
    private readonly IRemoteDropTargetResolver _dropTargetResolver;
    private readonly IClipboardFileProvider _clipboardFileProvider;
    private readonly FileTransferReceiver _fileReceiver;
    private readonly SemaphoreSlim _clipboardSendGate = new(1, 1);
    private readonly ClipboardTextReceiver _clipboardTextReceiver = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DirectoryBrowseResponse>> _browseRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ClipboardFilesResponse>> _clipboardRequests = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FileTransferDecision>> _uploadDecisions = [];
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<FileTransferResult>> _uploadResults = [];
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _outgoingUploadCancellations = [];
    private readonly ConcurrentDictionary<Guid, PendingDownload> _downloadRequests = [];
    private readonly ConcurrentDictionary<Guid, PendingDownload> _downloadsByTransfer = [];
    private readonly ConcurrentDictionary<Guid, PreparedFileTransfer> _outgoingDownloads = [];
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _outgoingDownloadCancellations = [];
    private readonly CancellationTokenSource _lifetime = new();
    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private Task? _acceptLoop;
    private volatile FramedMessageStream? _activeMessages;
    private CancellationTokenSource? _activeSessionCancellation;
    private volatile bool _clipboardTextAllowed;
    private volatile ClipboardSyncMode _clipboardSyncMode = ClipboardSyncMode.Off;

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

    public event Action<string>? ClipboardTextReceived;

    public event Action<ClipboardSyncMode>? ClipboardSyncModeChanged;

    public event Action<SessionReady>? SessionReadyReceived;

    public event Action? SessionEnded;

    public IPEndPoint? ListeningEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    public bool IsConnected => _activeMessages is not null &&
                               _activeSessionCancellation?.IsCancellationRequested == false;

    public bool FileTransferAllowed { get; private set; }

    public bool CanSendClipboardText =>
        _activeMessages is not null &&
        _clipboardTextAllowed &&
        _clipboardSyncMode == ClipboardSyncMode.Bidirectional;

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

    public async Task<DirectoryBrowseResponse> BrowseRemoteAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        FramedMessageStream messages = GetFileTransferMessages();
        Guid requestId = Guid.NewGuid();
        TaskCompletionSource<DirectoryBrowseResponse> completion = NewCompletion<DirectoryBrowseResponse>();
        if (!_browseRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("無法建立遠端目錄瀏覽要求。");
        }

        try
        {
            await messages.WriteAsync(
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

    public async Task<IReadOnlyList<string>> GetRemoteClipboardFilesAsync(
        CancellationToken cancellationToken = default)
    {
        FramedMessageStream messages = GetFileTransferMessages();
        Guid requestId = Guid.NewGuid();
        TaskCompletionSource<ClipboardFilesResponse> completion = NewCompletion<ClipboardFilesResponse>();
        if (!_clipboardRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("無法建立遠端剪貼簿要求。");
        }

        try
        {
            await messages.WriteAsync(
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

    public Task<FileTransferResult> UploadAsync(
        IReadOnlyList<string> sourcePaths,
        string remoteDestination,
        CancellationToken cancellationToken = default) =>
        UploadAsync(
            sourcePaths,
            remoteDestination,
            FileConflictBehavior.KeepBoth,
            cancellationToken);

    public async Task<FileTransferResult> UploadAsync(
        IReadOnlyList<string> sourcePaths,
        string remoteDestination,
        FileConflictBehavior conflictBehavior,
        CancellationToken cancellationToken = default)
    {
        FramedMessageStream messages = GetFileTransferMessages();
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
        CancellationTokenSource transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _activeSessionCancellation?.Token ?? CancellationToken.None);
        if (!_uploadDecisions.TryAdd(transfer.TransferId, decisionCompletion) ||
            !_uploadResults.TryAdd(transfer.TransferId, resultCompletion) ||
            !_outgoingUploadCancellations.TryAdd(transfer.TransferId, transferCancellation))
        {
            _uploadDecisions.TryRemove(transfer.TransferId, out _);
            _uploadResults.TryRemove(transfer.TransferId, out _);
            _outgoingUploadCancellations.TryRemove(transfer.TransferId, out _);
            transferCancellation.Dispose();
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
                transfer.ManifestSha256,
                conflictBehavior);
            await messages.WriteAsync(
                MessageType.FileUploadOffer,
                PayloadJson.Serialize(offer),
                transferCancellation.Token).ConfigureAwait(false);
            FileTransferDecision decision =
                await decisionCompletion.Task.WaitAsync(transferCancellation.Token).ConfigureAwait(false);
            if (!decision.Accepted)
            {
                return new FileTransferResult(
                    transfer.TransferId,
                    false,
                    decision.Reason ?? "遠端拒絕檔案傳輸。",
                    []);
            }

            await FileTransferSender.SendAsync(
                transfer,
                decision.ResumePoints,
                messages,
                MessageType.FileUploadChunk,
                FileTransferDirection.Upload,
                progress => FileTransferProgressChanged?.Invoke(progress),
                transferCancellation.Token).ConfigureAwait(false);
            await messages.WriteAsync(
                MessageType.FileUploadComplete,
                PayloadJson.Serialize(new FileTransferComplete(transfer.TransferId)),
                transferCancellation.Token).ConfigureAwait(false);
            FileTransferResult result =
                await resultCompletion.Task.WaitAsync(transferCancellation.Token).ConfigureAwait(false);
            completed = result.Succeeded;
            return result;
        }
        finally
        {
            _uploadDecisions.TryRemove(transfer.TransferId, out _);
            _uploadResults.TryRemove(transfer.TransferId, out _);
            _outgoingUploadCancellations.TryRemove(transfer.TransferId, out _);
            if (!completed && transferCancellation.IsCancellationRequested)
            {
                await TrySendCancelAsync(
                    transfer.TransferId,
                    FileTransferDirection.Upload,
                    deletePartialFiles: false).ConfigureAwait(false);
            }

            transferCancellation.Dispose();
        }
    }

    public Task<FileTransferResult> DownloadAsync(
        IReadOnlyList<string> remoteSourcePaths,
        string localDestination,
        CancellationToken cancellationToken = default) =>
        DownloadAsync(
            remoteSourcePaths,
            localDestination,
            FileConflictBehavior.KeepBoth,
            cancellationToken);

    public async Task<FileTransferResult> DownloadAsync(
        IReadOnlyList<string> remoteSourcePaths,
        string localDestination,
        FileConflictBehavior conflictBehavior,
        CancellationToken cancellationToken = default)
    {
        FramedMessageStream messages = GetFileTransferMessages();
        _ = FileTransferPolicy.ValidateExistingDirectory(localDestination);
        Guid requestId = Guid.NewGuid();
        Guid transferId = FileTransferIdentity.CreateSeed("download", localDestination, remoteSourcePaths);
        PendingDownload pending = new(localDestination, conflictBehavior);
        if (!_downloadRequests.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException("無法建立下載要求。");
        }

        try
        {
            await messages.WriteAsync(
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
        FramedMessageStream messages = GetFileTransferMessages();
        await _fileReceiver.CancelAsync(transferId, deletePartialFiles, cancellationToken)
            .ConfigureAwait(false);

        await messages.WriteAsync(
            MessageType.FileTransferCancel,
            PayloadJson.Serialize(new FileTransferCancel(transferId, direction, deletePartialFiles)),
            cancellationToken).ConfigureAwait(false);
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
            PairingRequest request = new(
                remoteEndpoint,
                hello.DeviceName,
                hello.OperatingSystem,
                pairingCode,
                hello.FileTransferRequested);
            PairingApproval approval = new(false, false);
            if (PairingApprovalHandler is not null)
            {
                using CancellationTokenSource approvalTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
                approvalTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                approval = await PairingApprovalHandler(request, approvalTimeout.Token).ConfigureAwait(false);
            }

            bool fileTransferAllowed = hello.FileTransferRequested && approval.FileTransferAllowed;
            SessionDecision decision = approval.Accepted
                ? new SessionDecision(
                    true,
                    null,
                    fileTransferAllowed,
                    approval.ClipboardTextAllowed)
                : new SessionDecision(false, "被控端未核准本次控制。", false, false);
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

            _activeMessages = messages;
            _activeSessionCancellation = sessionCancellation;
            FileTransferAllowed = fileTransferAllowed;
            _clipboardTextAllowed = approval.ClipboardTextAllowed;
            _clipboardSyncMode = ClipboardSyncMode.Off;
            _clipboardTextReceiver.Reset();
            SessionReadyReceived?.Invoke(ready);
            try
            {
                Task reader = ReadClientMessagesAsync(
                    messages,
                    fileTransferAllowed,
                    sessionCancellation.Token);
                Task sender = SendFramesAsync(messages, frames, firstFrame, sessionCancellation.Token);
                await Task.WhenAny(reader, sender).ConfigureAwait(false);
                sessionCancellation.Cancel();
                CancelOutgoingDownloads();
                await IgnoreExpectedSessionEndAsync(reader, sender).ConfigureAwait(false);
                await _fileReceiver.SuspendAllAsync(CancellationToken.None).ConfigureAwait(false);
                OnStatus("控制 session 已結束，等待下一次連線。");
            }
            finally
            {
                sessionCancellation.Cancel();
                try
                {
                    await _inputInjector.InjectAsync(
                        RemoteInputEvent.ReleaseAllKeys(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    OnStatus($"遠端按鍵釋放失敗：{exception.Message}");
                }

                FileTransferAllowed = false;
                CancelPendingRequests();
                SessionEnded?.Invoke();
                await ResetClipboardSessionAsync().ConfigureAwait(false);
            }
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
                case MessageType.DirectoryBrowseResponse:
                    DirectoryBrowseResponse remoteBrowse =
                        PayloadJson.Deserialize<DirectoryBrowseResponse>(packet.Payload);
                    if (_browseRequests.TryGetValue(
                            remoteBrowse.RequestId,
                            out TaskCompletionSource<DirectoryBrowseResponse>? browseCompletion))
                    {
                        browseCompletion.TrySetResult(remoteBrowse);
                    }

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
                case MessageType.ClipboardFilesResponse:
                    ClipboardFilesResponse remoteClipboard =
                        PayloadJson.Deserialize<ClipboardFilesResponse>(packet.Payload);
                    if (_clipboardRequests.TryGetValue(
                            remoteClipboard.RequestId,
                            out TaskCompletionSource<ClipboardFilesResponse>? clipboardCompletion))
                    {
                        clipboardCompletion.TrySetResult(remoteClipboard);
                    }

                    break;
                case MessageType.ClipboardModeChanged:
                    ClipboardModeChanged modeChange =
                        PayloadJson.Deserialize<ClipboardModeChanged>(packet.Payload);
                    await ApplyClipboardModeAsync(modeChange.Mode, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case MessageType.ClipboardTextOffer:
                    EnsureClipboardReceiveAvailable();
                    _clipboardTextReceiver.Begin(
                        PayloadJson.Deserialize<ClipboardTextOffer>(packet.Payload));
                    break;
                case MessageType.ClipboardTextChunk:
                    EnsureClipboardReceiveAvailable();
                    _clipboardTextReceiver.Append(
                        ClipboardTextChunkPayload.Deserialize(packet.Payload));
                    break;
                case MessageType.ClipboardTextComplete:
                    EnsureClipboardReceiveAvailable();
                    string clipboardText = _clipboardTextReceiver.Complete(
                        PayloadJson.Deserialize<ClipboardTextComplete>(packet.Payload));
                    ClipboardTextReceived?.Invoke(clipboardText);
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
                case MessageType.FileUploadDecision:
                    FileTransferDecision remoteUploadDecision =
                        PayloadJson.Deserialize<FileTransferDecision>(packet.Payload);
                    if (_uploadDecisions.TryGetValue(
                            remoteUploadDecision.TransferId,
                            out TaskCompletionSource<FileTransferDecision>? uploadDecisionCompletion))
                    {
                        uploadDecisionCompletion.TrySetResult(remoteUploadDecision);
                    }

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
                case MessageType.FileUploadResult:
                    FileTransferResult remoteUploadResult =
                        PayloadJson.Deserialize<FileTransferResult>(packet.Payload);
                    FileTransferCompleted?.Invoke(remoteUploadResult);
                    if (_uploadResults.TryGetValue(
                            remoteUploadResult.TransferId,
                            out TaskCompletionSource<FileTransferResult>? uploadResultCompletion))
                    {
                        uploadResultCompletion.TrySetResult(remoteUploadResult);
                    }

                    break;
                case MessageType.FileDownloadRequest:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileDownloadRequest downloadRequest =
                        PayloadJson.Deserialize<FileDownloadRequest>(packet.Payload);
                    _ = PrepareDownloadSafelyAsync(messages, downloadRequest, cancellationToken);
                    break;
                case MessageType.FileDownloadOffer:
                    await HandleRequestedDownloadOfferAsync(
                        messages,
                        PayloadJson.Deserialize<FileDownloadOffer>(packet.Payload),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case MessageType.FileDownloadDecision:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferDecision downloadDecision =
                        PayloadJson.Deserialize<FileTransferDecision>(packet.Payload);
                    StartDownload(messages, downloadDecision, cancellationToken);
                    break;
                case MessageType.FileDownloadChunk:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferProgress requestedDownloadProgress = await _fileReceiver.ReceiveChunkAsync(
                        FileChunkPayload.Deserialize(packet.Payload),
                        FileTransferDirection.Download,
                        cancellationToken).ConfigureAwait(false);
                    FileTransferProgressChanged?.Invoke(requestedDownloadProgress);
                    break;
                case MessageType.FileDownloadComplete:
                    EnsureFileTransferAllowed(fileTransferAllowed);
                    FileTransferComplete requestedDownloadComplete =
                        PayloadJson.Deserialize<FileTransferComplete>(packet.Payload);
                    _ = CompleteRequestedDownloadSafelyAsync(
                        messages,
                        requestedDownloadComplete,
                        cancellationToken);
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
                        if (_outgoingUploadCancellations.TryGetValue(
                                cancel.TransferId,
                                out CancellationTokenSource? uploadCts))
                        {
                            uploadCts.Cancel();
                        }
                        else
                        {
                            await _fileReceiver.CancelAsync(
                                cancel.TransferId,
                                cancel.DeletePartialFiles,
                                cancellationToken).ConfigureAwait(false);
                        }
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

    private async Task HandleRequestedDownloadOfferAsync(
        FramedMessageStream messages,
        FileDownloadOffer offer,
        CancellationToken cancellationToken)
    {
        if (!_downloadRequests.TryGetValue(offer.RequestId, out PendingDownload? pending))
        {
            await messages.WriteAsync(
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
        FileTransferDecision decision = await _fileReceiver.AcceptDownloadAsync(
            offer,
            pending.DestinationPath,
            pending.ConflictBehavior,
            cancellationToken).ConfigureAwait(false);
        await messages.WriteAsync(
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

    private async Task CompleteRequestedDownloadSafelyAsync(
        FramedMessageStream messages,
        FileTransferComplete complete,
        CancellationToken cancellationToken)
    {
        FileTransferResult result;
        try
        {
            result = await _fileReceiver.CompleteAsync(complete.TransferId, cancellationToken)
                .ConfigureAwait(false);
            await messages.WriteAsync(
                MessageType.FileDownloadResult,
                PayloadJson.Serialize(result),
                cancellationToken).ConfigureAwait(false);
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

    public async ValueTask<bool> SendClipboardTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!CanSendClipboardText)
        {
            return false;
        }

        await _clipboardSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FramedMessageStream? messages = _activeMessages;
            CancellationTokenSource? sessionCancellation = _activeSessionCancellation;
            if (messages is null || sessionCancellation is null || !CanSendClipboardText)
            {
                return false;
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                sessionCancellation.Token);
            await ClipboardTextSender.SendAsync(messages, text, linked.Token)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            _clipboardSendGate.Release();
        }
    }

    private async Task ApplyClipboardModeAsync(
        ClipboardSyncMode mode,
        CancellationToken cancellationToken)
    {
        ClipboardTextReceiver.ValidateMode(mode);
        if (mode != ClipboardSyncMode.Off && !_clipboardTextAllowed)
        {
            throw new UnauthorizedAccessException(
                "被控端未允許本次 session 的文字剪貼簿同步。");
        }

        await _clipboardSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _clipboardSyncMode = mode;
            if (mode == ClipboardSyncMode.Off)
            {
                _clipboardTextReceiver.Reset();
            }

            ClipboardSyncModeChanged?.Invoke(mode);
        }
        finally
        {
            _clipboardSendGate.Release();
        }
    }

    private void EnsureClipboardReceiveAvailable()
    {
        if (!_clipboardTextAllowed || _clipboardSyncMode == ClipboardSyncMode.Off)
        {
            _clipboardTextReceiver.Reset();
            throw new UnauthorizedAccessException(
                "本次 session 未啟用控制端到被控端的文字剪貼簿同步。");
        }
    }

    private async Task ResetClipboardSessionAsync()
    {
        await _clipboardSendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _activeMessages = null;
            _activeSessionCancellation = null;
            _clipboardTextAllowed = false;
            _clipboardSyncMode = ClipboardSyncMode.Off;
            _clipboardTextReceiver.Reset();
            ClipboardSyncModeChanged?.Invoke(ClipboardSyncMode.Off);
        }
        finally
        {
            _clipboardSendGate.Release();
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

    private FramedMessageStream GetFileTransferMessages()
    {
        if (!IsConnected || _activeMessages is null)
        {
            throw new InvalidOperationException("請先建立已配對的連線。");
        }

        if (!FileTransferAllowed)
        {
            throw new UnauthorizedAccessException("本次連線未取得雙方的檔案傳輸授權。");
        }

        return _activeMessages;
    }

    private async Task TrySendCancelAsync(
        Guid transferId,
        FileTransferDirection direction,
        bool deletePartialFiles)
    {
        try
        {
            if (IsConnected && _activeMessages is not null)
            {
                await _activeMessages.WriteAsync(
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

    private void CancelPendingRequests()
    {
        Exception exception = new IOException("檔案工作階段已中斷。");
        foreach (TaskCompletionSource<DirectoryBrowseResponse> pending in _browseRequests.Values)
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

        foreach (CancellationTokenSource cancellation in _outgoingUploadCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        foreach (PendingDownload pending in _downloadRequests.Values)
        {
            pending.Completion.TrySetException(exception);
        }

        _browseRequests.Clear();
        _clipboardRequests.Clear();
        _uploadDecisions.Clear();
        _uploadResults.Clear();
        _outgoingUploadCancellations.Clear();
        _downloadRequests.Clear();
        _downloadsByTransfer.Clear();
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

    public Task DisconnectSessionAsync()
    {
        _activeSessionCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private void OnStatus(string message) => StatusChanged?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _certificate?.Dispose();
        await _screenFrameSource.DisposeAsync().ConfigureAwait(false);
        _clipboardSendGate.Dispose();
        _lifetime.Dispose();
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PendingDownload(
        string destinationPath,
        FileConflictBehavior conflictBehavior)
    {
        public string DestinationPath { get; } = destinationPath;

        public FileConflictBehavior ConflictBehavior { get; } = conflictBehavior;

        public Guid? TransferId { get; set; }

        public TaskCompletionSource<FileTransferResult> Completion { get; } =
            NewCompletion<FileTransferResult>();
    }
}
