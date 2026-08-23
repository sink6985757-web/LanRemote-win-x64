using LanRemote.Protocol;

namespace LanRemote.Core;

public interface IScreenFrameSource : IAsyncDisposable
{
    VideoCodec Codec { get; }

    QualityProfile CurrentQualityProfile { get; }

    void ApplyQualityProfile(QualityProfile profile);

    IAsyncEnumerable<VideoFramePayload> CaptureAsync(CancellationToken cancellationToken);
}

public interface IInputInjector
{
    ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken);
}

public interface ISecureAttentionProvider
{
    ValueTask<SecureAttentionResult> RequestAsync(
        SecureAttentionRequest request,
        CancellationToken cancellationToken);
}

public interface IRemoteDropTargetResolver
{
    ValueTask<string?> ResolveAsync(float normalizedX, float normalizedY, CancellationToken cancellationToken);
}

public interface IClipboardFileProvider
{
    ValueTask<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken);
}

public sealed class UnavailableRemoteDropTargetResolver : IRemoteDropTargetResolver
{
    public ValueTask<string?> ResolveAsync(
        float normalizedX,
        float normalizedY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
}

public sealed class EmptyClipboardFileProvider : IClipboardFileProvider
{
    public ValueTask<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<string>>([]);
    }
}

public sealed class UnavailableSecureAttentionProvider : ISecureAttentionProvider
{
    public ValueTask<SecureAttentionResult> RequestAsync(
        SecureAttentionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SecureAttentionResult(
            request.RequestId,
            false,
            "SAS 服務尚未安裝或啟動；請在被控端以系統管理員身分執行 install-sas-service.ps1。"));
    }
}

public sealed class NullInputInjector : IInputInjector
{
    public ValueTask InjectAsync(RemoteInputEvent inputEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
