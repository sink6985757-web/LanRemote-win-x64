using System.Net;

namespace LanRemote.Core;

public sealed record PairingRequest(
    IPEndPoint RemoteEndpoint,
    string DeviceName,
    string OperatingSystem,
    string PairingCode);

public sealed record PairingApproval(bool Accepted, bool FileTransferAllowed);
