using LanRemote.Protocol;

namespace LanRemote.Core;

public interface IScreenFrameSource : IAsyncDisposable
{
    VideoCodec Codec { get; }

    IAsyncEnumerable<VideoFramePayload> CaptureAsync(CancellationToken cancellationToken);
}

public interface IInputInjector
{
    ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken);
}

public sealed class NullInputInjector : IInputInjector
{
    public ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
