using System.Net;

namespace LanRemote.Core;

public sealed record PairingRequest(
    IPEndPoint RemoteEndpoint,
    string DeviceName,
    string OperatingSystem,
    string PairingCode,
    bool FileTransferRequested = true);

public sealed record PairingApproval(
    bool Accepted,
    bool FileTransferAllowed,
    bool ClipboardTextAllowed = false);
