namespace LanRemote.Protocol;

public enum MessageType : byte
{
    ClientHello = 1,
    ServerHello = 2,
    SessionDecision = 3,
    SessionReady = 4,
    VideoFrame = 5,
    InputEvent = 6,
    Ping = 7,
    Pong = 8,
    Disconnect = 9,
    Error = 10,
    Metrics = 11,
    QualityProfileRequest = 12,
    QualityProfileApplied = 13,
}
