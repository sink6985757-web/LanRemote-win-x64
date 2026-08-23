using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Tests;

public sealed class RemoteSessionTests
{
    [Fact]
    public async Task ConnectAsync_TimesOutWhenTlsHandshakeDoesNotRespond()
    {
        using CancellationTokenSource testTimeout = new(TimeSpan.FromSeconds(5));
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            IPEndPoint endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
            Task<TcpClient> acceptedTask = listener.AcceptTcpClientAsync(testTimeout.Token).AsTask();
            RemoteConnectionTimeouts timeouts = new(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
            await using RemoteController controller = new(connectionTimeouts: timeouts);

            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
                controller.ConnectAsync(endpoint, testTimeout.Token));
            using TcpClient accepted = await acceptedTask;

            Assert.Contains("連線或安全交握", exception.Message, StringComparison.Ordinal);
            Assert.False(controller.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ConnectAsync_UsesSeparatePairingApprovalTimeout()
    {
        using CancellationTokenSource testTimeout = new(TimeSpan.FromSeconds(5));
        await using RemoteHost host = new(new FakeScreenFrameSource(), new NullInputInjector())
        {
            PairingApprovalHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new PairingApproval(true, false);
            },
        };
        await host.StartAsync(0, testTimeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
        RemoteConnectionTimeouts timeouts = new(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(2));
        await using RemoteController controller = new(connectionTimeouts: timeouts);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            controller.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, endpoint.Port),
                testTimeout.Token));

        Assert.Contains("等待被控端核准", exception.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(15), RemoteConnectionTimeouts.Default.EstablishmentTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), RemoteConnectionTimeouts.Default.PairingApprovalTimeout);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public void TransferIdentityIncludesManifestAndSupportsDriveRoot()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        string combined = FileTransferPolicy.CombineUnderRoot(root, "LanRemote-test.bin");
        Assert.Equal(Path.Combine(root, "LanRemote-test.bin"), combined, ignoreCase: true);

        Guid seed = FileTransferIdentity.CreateSeed("upload", Path.GetTempPath(), [combined]);
        Guid first = FileTransferIdentity.WithManifest(seed, new string('A', 64));
        Guid second = FileTransferIdentity.WithManifest(seed, new string('B', 64));
        Assert.NotEqual(first, second);
        Assert.Equal(first, FileTransferIdentity.WithManifest(seed, new string('a', 64)));
    }

    [Fact]
    public async Task ApprovedSession_MatchesPairingCode_ThenTransfersFrameAndInput()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        FakeScreenFrameSource screen = new();
        RecordingInputInjector input = new();
        await using RemoteHost host = new(screen, input);
        host.StatusChanged += message => Console.WriteLine($"HOST: {message}");
        string? hostPairingCode = null;
        host.PairingApprovalHandler = (request, _) =>
        {
            hostPairingCode = request.PairingCode;
            return Task.FromResult(new PairingApproval(true, true));
        };
        await host.StartAsync(0, timeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);

        await using RemoteController controller = new();
        string? controllerPairingCode = null;
        TaskCompletionSource<VideoFramePayload> frameReceived = NewCompletion<VideoFramePayload>();
        controller.PairingCodeAvailable += code => controllerPairingCode = code;
        controller.VideoFrameReceived += frame => frameReceived.TrySetResult(frame);

        await controller.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, endpoint.Port),
            QualityPreset.Smooth,
            timeout.Token);
        VideoFramePayload frame = await frameReceived.Task.WaitAsync(timeout.Token);
        RemoteInputEvent expectedInput = RemoteInputEvent.PhysicalKey(0x1E, isExtended: false, isDown: true);
        await controller.SendInputAsync(expectedInput, timeout.Token);
        RemoteInputEvent actualInput = await input.ReadAsync(timeout.Token);

        Assert.NotNull(hostPairingCode);
        Assert.Matches("^[0-9]{6}$", hostPairingCode);
        Assert.Equal(hostPairingCode, controllerPairingCode);
        Assert.Equal(VideoCodec.Jpeg, frame.Codec);
        Assert.Equal(screen.Frame.Data, frame.Data);
        Assert.Equal(expectedInput, actualInput);
        Assert.Equal(QualityProfiles.Smooth, screen.CurrentQualityProfile);
        Assert.Equal(QualityProfiles.Smooth, controller.CurrentQualityProfile);

        RemoteInputEvent expectedUnicode = RemoteInputEvent.UnicodeText(0x1F642);
        await controller.SendInputAsync(expectedUnicode, timeout.Token);
        Assert.Equal(expectedUnicode, await input.ReadAsync(timeout.Token));

        await controller.SendShortcutAsync(RemoteShortcut.ControlPaste, timeout.Token);
        List<RemoteInputEvent> clipboardInputs = [];
        for (int index = 0; index < 4; index++)
        {
            clipboardInputs.Add(await input.ReadAsync(timeout.Token));
        }

        Assert.Equal(RemoteShortcut.ControlPaste.ToInputEvents(), clipboardInputs);

        TaskCompletionSource<QualityProfile> qualityApplied = NewCompletion<QualityProfile>();
        controller.QualityProfileAppliedReceived += profile =>
        {
            if (profile.Preset == QualityPreset.Quality)
            {
                qualityApplied.TrySetResult(profile);
            }
        };
        await controller.ChangeQualityProfileAsync(QualityPreset.Quality, timeout.Token);
        QualityProfile applied = await qualityApplied.Task.WaitAsync(timeout.Token);
        Assert.Equal(QualityProfiles.Quality, applied);
        Assert.Equal(QualityProfiles.Quality, screen.CurrentQualityProfile);
        await controller.DisconnectAsync();
        Assert.Equal(RemoteInputKind.ReleaseAllKeys, (await input.ReadAsync(timeout.Token)).Kind);
    }

    [Fact]
    public async Task ApprovedClipboardText_SynchronizesBothDirectionsAndHonorsModes()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        FakeScreenFrameSource screen = new();
        await using RemoteHost host = new(screen, new NullInputInjector())
        {
            PairingApprovalHandler = (_, _) =>
                Task.FromResult(new PairingApproval(true, false, true)),
        };
        Channel<ClipboardSyncMode> modes = Channel.CreateUnbounded<ClipboardSyncMode>();
        host.ClipboardSyncModeChanged += mode => modes.Writer.TryWrite(mode);
        TaskCompletionSource<string> hostText = NewCompletion<string>();
        host.ClipboardTextReceived += text => hostText.TrySetResult(text);
        await host.StartAsync(0, timeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);

        await using RemoteController controller = new();
        TaskCompletionSource<string> controllerText = NewCompletion<string>();
        controller.ClipboardTextReceived += text => controllerText.TrySetResult(text);
        await controller.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, endpoint.Port),
            timeout.Token);

        Assert.True(controller.ClipboardTextAllowed);
        Assert.Equal(
            ClipboardSyncMode.Bidirectional,
            await modes.Reader.ReadAsync(timeout.Token));

        string controllerValue = new string('C', ProtocolConstants.ClipboardTextChunkLength + 9) + "主控端";
        Assert.True(await controller.SendClipboardTextAsync(controllerValue, timeout.Token));
        Assert.Equal(controllerValue, await hostText.Task.WaitAsync(timeout.Token));

        const string hostValue = "被控端複製的 Unicode 純文字";
        Assert.True(await host.SendClipboardTextAsync(hostValue, timeout.Token));
        Assert.Equal(hostValue, await controllerText.Task.WaitAsync(timeout.Token));

        await controller.ChangeClipboardSyncModeAsync(
            ClipboardSyncMode.ControllerToHost,
            timeout.Token);
        Assert.Equal(
            ClipboardSyncMode.ControllerToHost,
            await modes.Reader.ReadAsync(timeout.Token));
        Assert.False(host.CanSendClipboardText);
        Assert.False(await host.SendClipboardTextAsync("blocked", timeout.Token));

        await controller.ChangeClipboardSyncModeAsync(ClipboardSyncMode.Off, timeout.Token);
        Assert.Equal(ClipboardSyncMode.Off, await modes.Reader.ReadAsync(timeout.Token));
        Assert.False(await controller.SendClipboardTextAsync("blocked", timeout.Token));
        await controller.DisconnectAsync();
    }

    [Fact]
    public async Task RejectedSession_DoesNotStartScreenCapture()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        FakeScreenFrameSource screen = new();
        await using RemoteHost host = new(screen, new NullInputInjector())
        {
            PairingApprovalHandler = (_, _) => Task.FromResult(new PairingApproval(false, false)),
        };
        host.StatusChanged += message => Console.WriteLine($"HOST: {message}");
        await host.StartAsync(0, timeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
        await using RemoteController controller = new();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.ConnectAsync(new IPEndPoint(IPAddress.Loopback, endpoint.Port), timeout.Token));

        Assert.Equal(0, screen.CaptureStarts);
    }

    [Fact]
    public async Task ApprovedSession_RoundTripsSecureAttentionResult()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        FakeScreenFrameSource screen = new();
        RecordingSecureAttentionProvider secureAttention = new();
        await using RemoteHost host = new(screen, new NullInputInjector(), secureAttention)
        {
            PairingApprovalHandler = (_, _) => Task.FromResult(new PairingApproval(true, false)),
        };
        await host.StartAsync(0, timeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);

        await using RemoteController controller = new();
        TaskCompletionSource<SecureAttentionResult> resultReceived =
            NewCompletion<SecureAttentionResult>();
        controller.SecureAttentionResultReceived += result => resultReceived.TrySetResult(result);
        await controller.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, endpoint.Port),
            timeout.Token);

        Assert.False(controller.FileTransferAllowed);
        Assert.False(controller.ClipboardTextAllowed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.BrowseRemoteAsync(null, timeout.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await controller.ChangeClipboardSyncModeAsync(
                ClipboardSyncMode.Bidirectional,
                timeout.Token));
        Assert.False(await controller.SendClipboardTextAsync("not authorized", timeout.Token));

        Guid? requestId = await controller.RequestSecureAttentionAsync(timeout.Token);
        SecureAttentionResult result = await resultReceived.Task.WaitAsync(timeout.Token);

        Assert.NotNull(requestId);
        Assert.Equal(requestId, secureAttention.LastRequestId);
        Assert.Equal(requestId, result.RequestId);
        Assert.True(result.Succeeded);
        Assert.Equal("accepted-for-test", result.Message);
        await controller.DisconnectAsync();
    }

    [Fact]
    public async Task ApprovedFileTransfer_UploadsDownloadsBrowsesAndAutoRenames()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string root = Path.Combine(Path.GetTempPath(), $"LanRemoteTransferTest-{Guid.NewGuid():N}");
        string sourceDirectory = Path.Combine(root, "source");
        string hostDestination = Path.Combine(root, "host-destination");
        string controllerDestination = Path.Combine(root, "controller-destination");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(hostDestination);
        Directory.CreateDirectory(controllerDestination);
        string sourcePath = Path.Combine(sourceDirectory, "payload.bin");
        byte[] expected = Enumerable.Range(0, ProtocolConstants.FileChunkLength + 12345)
            .Select(index => (byte)(index % 251))
            .ToArray();
        await File.WriteAllBytesAsync(sourcePath, expected, timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(hostDestination, "payload.bin"), "existing", timeout.Token);

        try
        {
            FakeScreenFrameSource screen = new();
            await using RemoteHost host = new(
                screen,
                new NullInputInjector(),
                fileReceiver: new FileTransferReceiver(Path.Combine(root, "host-state")))
            {
                PairingApprovalHandler = (_, _) =>
                    Task.FromResult(new PairingApproval(true, true)),
            };
            await host.StartAsync(0, timeout.Token);
            IPEndPoint listeningEndpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
            IPEndPoint endpoint = new(IPAddress.Loopback, listeningEndpoint.Port);
            await using RemoteController controller = new(
                new FileTransferReceiver(Path.Combine(root, "controller-state")));
            await controller.ConnectAsync(endpoint, timeout.Token);

            FileTransferResult upload = await controller.UploadAsync(
                [sourcePath],
                hostDestination,
                timeout.Token);
            Assert.True(upload.Succeeded, upload.Message);
            string uploadedPath = Path.Combine(hostDestination, "payload (1).bin");
            Assert.Equal(expected, await File.ReadAllBytesAsync(uploadedPath, timeout.Token));

            DirectoryBrowseResponse browse = await controller.BrowseRemoteAsync(hostDestination, timeout.Token);
            Assert.Null(browse.Error);
            Assert.Contains(browse.Entries, entry => entry.FullPath == uploadedPath);

            FileTransferResult download = await controller.DownloadAsync(
                [uploadedPath],
                controllerDestination,
                timeout.Token);
            Assert.True(download.Succeeded, download.Message);
            Assert.Equal(
                expected,
                await File.ReadAllBytesAsync(
                    Path.Combine(controllerDestination, "payload (1).bin"),
                    timeout.Token));
            await controller.DisconnectAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApprovedFileTransfer_BothEndpointsInitiateAndOppositeDirectionsRunTogether()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string root = Path.Combine(Path.GetTempPath(), $"LanRemoteDuplexTransferTest-{Guid.NewGuid():N}");
        string controllerSourceRoot = Path.Combine(root, "controller-source");
        string controllerDestination = Path.Combine(root, "controller-destination");
        string hostSourceRoot = Path.Combine(root, "host-source");
        string hostDestination = Path.Combine(root, "host-destination");
        Directory.CreateDirectory(controllerSourceRoot);
        Directory.CreateDirectory(controllerDestination);
        Directory.CreateDirectory(hostSourceRoot);
        Directory.CreateDirectory(hostDestination);
        string controllerSource = Path.Combine(controllerSourceRoot, "from-controller.bin");
        string hostSource = Path.Combine(hostSourceRoot, "from-host.bin");
        byte[] controllerBytes = Enumerable.Range(0, ProtocolConstants.FileChunkLength + 321)
            .Select(index => (byte)(index % 241))
            .ToArray();
        byte[] hostBytes = Enumerable.Range(0, ProtocolConstants.FileChunkLength + 777)
            .Select(index => (byte)(index % 233))
            .ToArray();
        await File.WriteAllBytesAsync(controllerSource, controllerBytes, timeout.Token);
        await File.WriteAllBytesAsync(hostSource, hostBytes, timeout.Token);

        try
        {
            await using RemoteHost host = new(
                new FakeScreenFrameSource(),
                new NullInputInjector(),
                fileReceiver: new FileTransferReceiver(Path.Combine(root, "host-state")))
            {
                PairingApprovalHandler = (_, _) => Task.FromResult(new PairingApproval(true, true)),
            };
            await host.StartAsync(0, timeout.Token);
            IPEndPoint listeningEndpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
            await using RemoteController controller = new(
                new FileTransferReceiver(Path.Combine(root, "controller-state")));
            await controller.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, listeningEndpoint.Port),
                QualityPreset.Balanced,
                fileTransferRequested: true,
                timeout.Token);

            Assert.True(host.IsConnected);
            Assert.True(host.FileTransferAllowed);
            Assert.True(controller.FileTransferAllowed);

            Task<FileTransferResult> controllerToHost = controller.UploadAsync(
                [controllerSource],
                hostDestination,
                FileConflictBehavior.KeepBoth,
                timeout.Token);
            Task<FileTransferResult> hostToController = host.UploadAsync(
                [hostSource],
                controllerDestination,
                FileConflictBehavior.KeepBoth,
                timeout.Token);
            FileTransferResult[] simultaneous = await Task.WhenAll(controllerToHost, hostToController);
            Assert.All(simultaneous, result => Assert.True(result.Succeeded, result.Message));
            Assert.Equal(
                controllerBytes,
                await File.ReadAllBytesAsync(
                    Path.Combine(hostDestination, "from-controller.bin"),
                    timeout.Token));
            Assert.Equal(
                hostBytes,
                await File.ReadAllBytesAsync(
                    Path.Combine(controllerDestination, "from-host.bin"),
                    timeout.Token));

            DirectoryBrowseResponse hostBrowse = await host.BrowseRemoteAsync(
                controllerDestination,
                timeout.Token);
            RemoteDirectoryEntry controllerReceived = Assert.Single(
                hostBrowse.Entries,
                entry => entry.Name == "from-host.bin");
            string hostDownloadDestination = Path.Combine(root, "host-download");
            Directory.CreateDirectory(hostDownloadDestination);
            FileTransferResult hostDownload = await host.DownloadAsync(
                [controllerReceived.FullPath],
                hostDownloadDestination,
                FileConflictBehavior.KeepBoth,
                timeout.Token);
            Assert.True(hostDownload.Succeeded, hostDownload.Message);
            Assert.Equal(
                hostBytes,
                await File.ReadAllBytesAsync(
                    Path.Combine(hostDownloadDestination, "from-host.bin"),
                    timeout.Token));

            await controller.DisconnectAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileTransfer_RequiresInitiatorRequestAndReceiverApproval()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        await using RemoteHost host = new(new FakeScreenFrameSource(), new NullInputInjector())
        {
            PairingApprovalHandler = (request, _) =>
            {
                Assert.False(request.FileTransferRequested);
                return Task.FromResult(new PairingApproval(true, true));
            },
        };
        await host.StartAsync(0, timeout.Token);
        IPEndPoint listeningEndpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
        await using RemoteController controller = new();
        await controller.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, listeningEndpoint.Port),
            QualityPreset.Balanced,
            fileTransferRequested: false,
            timeout.Token);

        Assert.False(controller.FileTransferAllowed);
        Assert.False(host.FileTransferAllowed);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            host.BrowseRemoteAsync(null, timeout.Token));
        await controller.DisconnectAsync();
    }

    [Theory]
    [InlineData(FileConflictBehavior.Overwrite, "new")]
    [InlineData(FileConflictBehavior.Skip, "old")]
    public async Task FileReceiver_AppliesExplicitConflictBehavior(
        FileConflictBehavior behavior,
        string expectedText)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        string root = Path.Combine(Path.GetTempPath(), $"LanRemoteConflictTest-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(root, "source");
        string destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destination);
        string sourcePath = Path.Combine(sourceRoot, "same.txt");
        string destinationPath = Path.Combine(destination, "same.txt");
        await File.WriteAllTextAsync(sourcePath, "new", timeout.Token);
        await File.WriteAllTextAsync(destinationPath, "old", timeout.Token);

        try
        {
            PreparedFileTransfer prepared = await FileTransferManifestBuilder.CreateAsync(
                [sourcePath],
                cancellationToken: timeout.Token);
            FileUploadOffer offer = new(
                prepared.TransferId,
                destination,
                prepared.Entries,
                prepared.TotalBytes,
                prepared.ManifestSha256,
                behavior);
            FileTransferReceiver receiver = new(Path.Combine(root, "state"));
            FileTransferDecision decision = await receiver.AcceptUploadAsync(offer, timeout.Token);
            Assert.True(decision.Accepted, decision.Reason);
            FileTransferEntry entry = Assert.Single(prepared.Entries);
            FileResumePoint resume = Assert.Single(decision.ResumePoints);
            if (!resume.Completed)
            {
                byte[] data = await File.ReadAllBytesAsync(sourcePath, timeout.Token);
                _ = await receiver.ReceiveChunkAsync(
                    new FileChunkPayload(prepared.TransferId, entry.Index, 0, data),
                    FileTransferDirection.Upload,
                    timeout.Token);
            }

            FileTransferResult result = await receiver.CompleteAsync(prepared.TransferId, timeout.Token);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(expectedText, await File.ReadAllTextAsync(destinationPath, timeout.Token));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileReceiver_ResumesPartialWithoutOverwritingDestination()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        string root = Path.Combine(Path.GetTempPath(), $"LanRemoteResumeTest-{Guid.NewGuid():N}");
        string sourceDirectory = Path.Combine(root, "source");
        string destination = Path.Combine(root, "destination");
        string state = Path.Combine(root, "state");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destination);
        string sourcePath = Path.Combine(sourceDirectory, "resume.bin");
        byte[] bytes = Enumerable.Range(0, ProtocolConstants.FileChunkLength + 100)
            .Select(index => (byte)(index % 239))
            .ToArray();
        await File.WriteAllBytesAsync(sourcePath, bytes, timeout.Token);

        try
        {
            PreparedFileTransfer prepared = await FileTransferManifestBuilder.CreateAsync(
                [sourcePath],
                cancellationToken: timeout.Token);
            FileUploadOffer offer = new(
                prepared.TransferId,
                destination,
                prepared.Entries,
                prepared.TotalBytes,
                prepared.ManifestSha256);
            FileTransferReceiver firstReceiver = new(state);
            FileTransferDecision firstDecision = await firstReceiver.AcceptUploadAsync(offer, timeout.Token);
            Assert.True(firstDecision.Accepted, firstDecision.Reason);
            FileTransferEntry file = Assert.Single(prepared.Entries);
            byte[] firstChunk = bytes[..ProtocolConstants.FileChunkLength];
            _ = await firstReceiver.ReceiveChunkAsync(
                new FileChunkPayload(prepared.TransferId, file.Index, 0, firstChunk),
                FileTransferDirection.Upload,
                timeout.Token);
            await firstReceiver.SuspendAllAsync(timeout.Token);

            FileTransferReceiver resumedReceiver = new(state);
            FileTransferDecision resumed = await resumedReceiver.AcceptUploadAsync(offer, timeout.Token);
            FileResumePoint resumePoint = Assert.Single(resumed.ResumePoints);
            Assert.Equal(firstChunk.Length, resumePoint.Offset);
            Assert.False(resumePoint.Completed);
            await resumedReceiver.CancelAsync(prepared.TransferId, true, timeout.Token);
            Assert.Empty(Directory.EnumerateFiles(destination, "*.partial", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeScreenFrameSource : IScreenFrameSource
    {
        public VideoFramePayload Frame { get; } = new(
            2,
            2,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            VideoCodec.Jpeg,
            [1, 2, 3, 4]);

        public int CaptureStarts { get; private set; }

        public VideoCodec Codec => VideoCodec.Jpeg;

        public QualityProfile CurrentQualityProfile { get; private set; } = QualityProfiles.Balanced;

        public void ApplyQualityProfile(QualityProfile profile)
        {
            Assert.True(QualityProfiles.IsCanonical(profile));
            CurrentQualityProfile = profile;
        }

        public async IAsyncEnumerable<VideoFramePayload> CaptureAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CaptureStarts++;
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return Frame with
                {
                    CapturedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                await Task.Delay(10, cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingInputInjector : IInputInjector
    {
        private readonly Channel<RemoteInputEvent> _inputs = Channel.CreateUnbounded<RemoteInputEvent>();

        public async ValueTask<RemoteInputEvent> ReadAsync(CancellationToken cancellationToken) =>
            await _inputs.Reader.ReadAsync(cancellationToken);

        public ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_inputs.Writer.TryWrite(inputEvent))
            {
                throw new InvalidOperationException("無法記錄遠端輸入事件。");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSecureAttentionProvider : ISecureAttentionProvider
    {
        public Guid? LastRequestId { get; private set; }

        public ValueTask<SecureAttentionResult> RequestAsync(
            SecureAttentionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestId = request.RequestId;
            return ValueTask.FromResult(new SecureAttentionResult(
                request.RequestId,
                true,
                "accepted-for-test"));
        }
    }
}
