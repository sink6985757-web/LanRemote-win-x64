using System.Net;

namespace LanRemote.Core;

public sealed record PairingRequest(
    IPEndPoint RemoteEndpoint,
    string DeviceName,
    string OperatingSystem,
    string PairingCode);
