using System.Net;
using System.Runtime.CompilerServices;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Tests;

public sealed class RemoteSessionTests
{
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
        RemoteInputEvent expectedInput = new(RemoteInputKind.Key, IsDown: true, VirtualKey: 0x41);
        await controller.SendInputAsync(expectedInput, timeout.Token);
        RemoteInputEvent actualInput = await input.NextInput.Task.WaitAsync(timeout.Token);

        Assert.NotNull(hostPairingCode);
        Assert.Matches("^[0-9]{6}$", hostPairingCode);
        Assert.Equal(hostPairingCode, controllerPairingCode);
        Assert.Equal(VideoCodec.Jpeg, frame.Codec);
        Assert.Equal(screen.Frame.Data, frame.Data);
        Assert.Equal(expectedInput, actualInput);
        Assert.Equal(QualityProfiles.Smooth, screen.CurrentQualityProfile);
        Assert.Equal(QualityProfiles.Smooth, controller.CurrentQualityProfile);

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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.BrowseRemoteAsync(null, timeout.Token));

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
        public TaskCompletionSource<RemoteInputEvent> NextInput { get; } =
            NewCompletion<RemoteInputEvent>();

        public ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NextInput.TrySetResult(inputEvent);
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
