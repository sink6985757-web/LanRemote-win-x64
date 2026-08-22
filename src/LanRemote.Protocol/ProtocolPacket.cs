namespace LanRemote.Protocol;

public sealed record ProtocolPacket(MessageType Type, byte[] Payload);
