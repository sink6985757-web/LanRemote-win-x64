using System.Net;
using System.Runtime.CompilerServices;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Tests;

public sealed class RemoteSessionTests
{
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
            return Task.FromResult(true);
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
            PairingApprovalHandler = (_, _) => Task.FromResult(false),
        };
        host.StatusChanged += message => Console.WriteLine($"HOST: {message}");
        await host.StartAsync(0, timeout.Token);
        IPEndPoint endpoint = Assert.IsType<IPEndPoint>(host.ListeningEndpoint);
        await using RemoteController controller = new();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.ConnectAsync(new IPEndPoint(IPAddress.Loopback, endpoint.Port), timeout.Token));

        Assert.Equal(0, screen.CaptureStarts);
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
}
