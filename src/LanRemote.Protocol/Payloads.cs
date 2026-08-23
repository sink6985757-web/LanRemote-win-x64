namespace LanRemote.Protocol;

public sealed record ClientHello(
    int ProtocolVersion,
    string DeviceName,
    string OperatingSystem,
    string NonceBase64,
    QualityPreset InitialQualityPreset);

public sealed record ServerHello(
    int ProtocolVersion,
    string DeviceName,
    string OperatingSystem,
    string NonceBase64);

public sealed record SessionDecision(
    bool Accepted,
    string? Reason,
    bool FileTransferAllowed = false);

public sealed record SessionReady(
    Guid SessionId,
    int Width,
    int Height,
    VideoCodec Codec,
    QualityProfile QualityProfile);

public sealed record QualityProfileRequest(QualityPreset Preset);

public sealed record SecureAttentionRequest(Guid RequestId);

public sealed record SecureAttentionResult(Guid RequestId, bool Succeeded, string Message);

public sealed record ErrorPayload(string Code, string Message);

public sealed record MetricsPayload(
    long CapturedFrames,
    long SentFrames,
    long DroppedFrames,
    double LastEncodeMilliseconds,
    double LastFrameBytes);
