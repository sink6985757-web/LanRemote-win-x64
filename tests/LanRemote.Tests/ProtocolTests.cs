using System.Net;
using System.Security.Cryptography;
using System.Text;
using LanRemote.Core;
using LanRemote.Protocol;

namespace LanRemote.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task FramedStream_RoundTripsPacket()
    {
        Assert.Equal(6, ProtocolConstants.Version);
        Assert.True(ProtocolConstants.Magic.SequenceEqual("LRM6"u8));
        using MemoryStream transport = new();
        await using FramedMessageStream messages = new(transport);
        byte[] expected = [1, 3, 5, 7, 9];

        await messages.WriteAsync(MessageType.Ping, expected);
        transport.Position = 0;
        ProtocolPacket actual = await messages.ReadAsync();

        Assert.Equal(MessageType.Ping, actual.Type);
        Assert.Equal(expected, actual.Payload);
    }

    [Fact]
    public void ClipboardTextReceiver_ReassemblesChunkedUnicodeAndVerifiesHash()
    {
        string expected = new string('A', ProtocolConstants.ClipboardTextChunkLength + 17) + "雙向剪貼簿";
        byte[] bytes = Encoding.UTF8.GetBytes(expected);
        Guid updateId = Guid.NewGuid();
        ClipboardTextReceiver receiver = new();
        receiver.Begin(new ClipboardTextOffer(
            updateId,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes))));

        int firstLength = ProtocolConstants.ClipboardTextChunkLength;
        receiver.Append(new ClipboardTextChunkPayload(updateId, 0, bytes[..firstLength]));
        receiver.Append(new ClipboardTextChunkPayload(updateId, firstLength, bytes[firstLength..]));
        string actual = receiver.Complete(new ClipboardTextComplete(updateId));

        Assert.Equal(expected, actual);
        Assert.False(receiver.HasPendingUpdate);
    }

    [Fact]
    public void ClipboardTextReceiver_RejectsOversizeAndOutOfOrderUpdates()
    {
        ClipboardTextReceiver receiver = new();
        Assert.Throws<ProtocolException>(() => receiver.Begin(new ClipboardTextOffer(
            Guid.NewGuid(),
            ProtocolConstants.MaxClipboardTextLength + 1,
            new string('0', 64))));

        byte[] bytes = Encoding.UTF8.GetBytes("ordered");
        Guid updateId = Guid.NewGuid();
        receiver.Begin(new ClipboardTextOffer(
            updateId,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes))));
        Assert.Throws<ProtocolException>(() => receiver.Append(
            new ClipboardTextChunkPayload(updateId, 1, bytes)));
        receiver.Reset();
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
    [InlineData(QualityPreset.Smooth, 1280, 720, 60, 40)]
    [InlineData(QualityPreset.Balanced, 1600, 900, 48, 60)]
    [InlineData(QualityPreset.Quality, 1920, 1080, 30, 82)]
    public void QualityProfiles_ReturnConfirmedCanonicalValues(
        QualityPreset preset,
        int width,
        int height,
        int framesPerSecond,
        int jpegQuality)
    {
        QualityProfile profile = QualityProfiles.Get(preset);

        Assert.Equal(width, profile.MaximumWidth);
        Assert.Equal(height, profile.MaximumHeight);
        Assert.Equal(framesPerSecond, profile.FramesPerSecond);
        Assert.Equal(jpegQuality, profile.JpegQuality);
        Assert.True(QualityProfiles.IsCanonical(profile));
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

    [Fact]
    public void AspectFitMapper_NormalizesCenterAcrossWindowSizes()
    {
        Assert.True(AspectFitMapper.TryNormalizePoint(
            1920,
            1080,
            1000,
            1000,
            500,
            500,
            out float squareX,
            out float squareY));
        Assert.Equal(0.5f, squareX, precision: 4);
        Assert.Equal(0.5f, squareY, precision: 4);

        Assert.True(AspectFitMapper.TryNormalizePoint(
            1920,
            1080,
            1600,
            700,
            800,
            350,
            out float wideX,
            out float wideY));
        Assert.Equal(0.5f, wideX, precision: 4);
        Assert.Equal(0.5f, wideY, precision: 4);
    }

    [Fact]
    public void AspectFitMapper_RejectsLetterboxArea()
    {
        Assert.False(AspectFitMapper.TryNormalizePoint(
            1920,
            1080,
            1000,
            1000,
            500,
            100,
            out _,
            out _));
    }

    [Theory]
    [InlineData(RemoteScaleMode.Stretch, 0.25, 0.25)]
    [InlineData(RemoteScaleMode.Fit, 0.25, 0.0555556)]
    [InlineData(RemoteScaleMode.Crop, 0.359375, 0.25)]
    public void ViewportMapper_MapsScalingModes(
        RemoteScaleMode mode,
        double expectedX,
        double expectedY)
    {
        Assert.True(RemoteViewportMapper.TryNormalizePoint(
            mode,
            1920,
            1080,
            1000,
            1000,
            250,
            250,
            out float x,
            out float y));
        Assert.Equal(expectedX, x, precision: 4);
        Assert.Equal(expectedY, y, precision: 4);
    }

    [Fact]
    public void RemoteShortcut_ProducesOrderedKeySequence()
    {
        IReadOnlyList<RemoteInputEvent> events = RemoteShortcut.ControlAltZero.ToInputEvents();

        Assert.Equal(6, events.Count);
        Assert.Equal((0x11, true), (events[0].VirtualKey, events[0].IsDown));
        Assert.Equal((0x12, true), (events[1].VirtualKey, events[1].IsDown));
        Assert.Equal((0x30, true), (events[2].VirtualKey, events[2].IsDown));
        Assert.Equal((0x30, false), (events[3].VirtualKey, events[3].IsDown));
        Assert.Equal((0x12, false), (events[4].VirtualKey, events[4].IsDown));
        Assert.Equal((0x11, false), (events[5].VirtualKey, events[5].IsDown));
    }

    [Theory]
    [InlineData(0x43, "Ctrl+C")]
    [InlineData(0x58, "Ctrl+X")]
    [InlineData(0x56, "Ctrl+V")]
    public void RemoteShortcut_ClipboardHotkeysProduceAtomicControlSequence(
        ushort virtualKey,
        string gesture)
    {
        Assert.True(RemoteShortcut.TryGetClipboardHotkey(virtualKey, out RemoteShortcut? shortcut));
        Assert.NotNull(shortcut);
        Assert.Equal(gesture, shortcut.GestureText);

        IReadOnlyList<RemoteInputEvent> events = shortcut.ToInputEvents();
        Assert.Equal(4, events.Count);
        Assert.Equal((0x11, true), (events[0].VirtualKey, events[0].IsDown));
        Assert.Equal((virtualKey, true), (events[1].VirtualKey, events[1].IsDown));
        Assert.Equal((virtualKey, false), (events[2].VirtualKey, events[2].IsDown));
        Assert.Equal((0x11, false), (events[3].VirtualKey, events[3].IsDown));
    }

    [Theory]
    [InlineData(0x2E, ShortcutModifiers.Control | ShortcutModifiers.Alt)]
    [InlineData(0x73, ShortcutModifiers.Alt)]
    [InlineData(0x1B, ShortcutModifiers.Control)]
    [InlineData(0x09, ShortcutModifiers.Alt)]
    [InlineData(0x20, ShortcutModifiers.Alt)]
    [InlineData(0x1B, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
    [InlineData(0x5B, ShortcutModifiers.None)]
    public void RemoteShortcut_RejectsReservedCombinations(
        ushort virtualKey,
        ShortcutModifiers modifiers)
    {
        Assert.False(RemoteShortcut.TryValidate(
            new RemoteShortcut("blocked", virtualKey, modifiers),
            out _));
    }

    [Fact]
    public void RemoteShortcutStore_RoundTripsValidEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"LanRemoteShortcutTest-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "hotkeys.json");
        try
        {
            RemoteShortcutStore store = new(path);
            RemoteShortcut expected = new(
                "測試 Ctrl+Shift+K",
                0x4B,
                ShortcutModifiers.Control | ShortcutModifiers.Shift);

            store.Save([expected]);
            RemoteShortcut actual = Assert.Single(store.Load());

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LatestValueBuffer_DropsStaleValuesWithoutGrowingAQueue()
    {
        LatestValueBuffer<string> buffer = new();

        buffer.Offer("frame-1");
        buffer.Offer("frame-2");
        buffer.Offer("frame-3");

        Assert.Equal(2, buffer.DroppedCount);
        Assert.Equal("frame-3", buffer.Take());
        Assert.False(buffer.HasValue);
    }

    [Fact]
    public void FileChunk_RoundTripsBinaryPayload()
    {
        FileChunkPayload expected = new(Guid.NewGuid(), 7, 123_456, [1, 2, 3, 4, 5]);

        FileChunkPayload actual = FileChunkPayload.Deserialize(expected.Serialize());

        Assert.Equal(expected.TransferId, actual.TransferId);
        Assert.Equal(expected.EntryIndex, actual.EntryIndex);
        Assert.Equal(expected.Offset, actual.Offset);
        Assert.Equal(expected.Data, actual.Data);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("folder/../../escape.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("file.txt:secret")]
    [InlineData("folder/trailing. ")]
    public void FileTransferPolicy_RejectsUnsafeRelativePaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => FileTransferPolicy.NormalizeRelativePath(path));
    }
}
