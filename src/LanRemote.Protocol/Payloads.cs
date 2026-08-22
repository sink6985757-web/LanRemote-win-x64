namespace LanRemote.Protocol;

public sealed record ClientHello(
    int ProtocolVersion,
    string DeviceName,
    string OperatingSystem,
    string NonceBase64);

public sealed record ServerHello(
    int ProtocolVersion,
    string DeviceName,
    string OperatingSystem,
    string NonceBase64);

public sealed record SessionDecision(bool Accepted, string? Reason);

public sealed record SessionReady(Guid SessionId, int Width, int Height, VideoCodec Codec);

public sealed record ErrorPayload(string Code, string Message);

public sealed record MetricsPayload(
    long CapturedFrames,
    long SentFrames,
    long DroppedFrames,
    double LastEncodeMilliseconds,
    double LastFrameBytes);
