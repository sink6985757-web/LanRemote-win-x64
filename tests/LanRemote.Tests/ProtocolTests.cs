using System.Net;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task FramedStream_RoundTripsPacket()
    {
        using MemoryStream transport = new();
        await using FramedMessageStream messages = new(transport);
        byte[] expected = [1, 3, 5, 7, 9];

        await messages.WriteAsync(MessageType.Ping, expected);
        transport.Position = 0;
        ProtocolPacket actual = await messages.ReadAsync();

        Assert.Equal(MessageType.Ping, actual.Type);
        Assert.Equal(expected, actual.Payload);
    }

    [Theory]
    [InlineData(RemoteInputKind.MouseMove)]
    [InlineData(RemoteInputKind.MouseButton)]
    [InlineData(RemoteInputKind.MouseWheel)]
    [InlineData(RemoteInputKind.Key)]
    public void RemoteInput_RoundTrips(RemoteInputKind kind)
    {
        RemoteInputEvent expected = new(
            kind,
            0.25f,
            0.75f,
            RemoteMouseButton.Right,
            true,
            -120,
            0x41);

        RemoteInputEvent actual = RemoteInputEvent.Deserialize(expected.Serialize());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void VideoFrame_RoundTrips()
    {
        VideoFramePayload expected = new(1920, 1080, 123456789, VideoCodec.Jpeg, [4, 3, 2, 1]);

        VideoFramePayload actual = VideoFramePayload.Deserialize(expected.Serialize());

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.CapturedAtUnixMilliseconds, actual.CapturedAtUnixMilliseconds);
        Assert.Equal(expected.Codec, actual.Codec);
        Assert.Equal(expected.Data, actual.Data);
    }

    [Theory]
    [InlineData("127.0.0.1:45873")]
    [InlineData("10.1.2.3:45873")]
    [InlineData("172.16.1.2:45873")]
    [InlineData("192.168.50.9:45873")]
    [InlineData("169.254.10.2:45873")]
    public void EndpointParser_AcceptsPrivateIpv4(string value)
    {
        IPEndPoint endpoint = LanEndpointParser.Parse(value);

        Assert.Equal(45873, endpoint.Port);
        Assert.True(LanAddressPolicy.IsAllowed(endpoint.Address));
    }

    [Theory]
    [InlineData("8.8.8.8:45873")]
    [InlineData("1.1.1.1:45873")]
    [InlineData("::1")]
    public void EndpointParser_RejectsNonLanOrNonIpv4(string value)
    {
        Assert.Throws<FormatException>(() => LanEndpointParser.Parse(value));
    }
}
