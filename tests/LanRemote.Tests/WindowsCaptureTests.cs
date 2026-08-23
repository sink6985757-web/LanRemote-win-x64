using LanRemote.Protocol;
using LanRemote.Windows;

namespace LanRemote.Tests;

public sealed class WindowsCaptureTests
{
    [Theory]
    [InlineData(0x74, 0x3F, false)] // F5
    [InlineData(0x24, 0x47, true)]  // Home
    [InlineData(0x23, 0x4F, true)]  // End
    [InlineData(0x25, 0x4B, true)]  // Left arrow
    [InlineData(0x09, 0x0F, false)] // Tab
    [InlineData(0x14, 0x3A, false)] // Caps Lock
    [InlineData(0x5B, 0x5B, true)]  // Left Windows
    [InlineData(0x5C, 0x5C, true)]  // Right Windows
    [InlineData(0xA3, 0x1D, true)]  // Right Ctrl
    [InlineData(0xA5, 0x38, true)]  // Right Alt
    public void KeyboardMapper_ReturnsScanCodeAndExtendedFlag(
        int virtualKey,
        ushort expectedScanCode,
        bool expectedExtended)
    {
        Assert.True(WindowsKeyboardMapper.TryMapVirtualKey(virtualKey, out PhysicalKeyDescriptor mapped));
        Assert.Equal(expectedScanCode, mapped.ScanCode);
        Assert.Equal(expectedExtended, mapped.IsExtended);
    }

    [Theory]
    [InlineData(0x1B)] // Escape
    [InlineData(0x7A)] // F11
    [InlineData(0x09)] // Tab (including the Alt+Tab sequence)
    [InlineData(0x14)] // Caps Lock
    [InlineData(0x5B)] // Left Windows
    [InlineData(0x5C)] // Right Windows
    [InlineData(0xA5)] // Right Alt
    public void KeyboardRouting_ForwardsEveryOrdinaryPhysicalKey(ushort virtualKey)
    {
        PhysicalKeyboardRoutingState state = new();

        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(virtualKey, isDown: true, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(virtualKey, isDown: false, isInjected: false, captureEnabled: true));
    }

    [Fact]
    public void KeyboardRouting_LeavesInjectedEventsForTheWpfFallback()
    {
        PhysicalKeyboardRoutingState state = new();

        Assert.Equal(
            PhysicalKeyboardDisposition.PassThrough,
            state.Process(0xA5, isDown: true, isInjected: true, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(0xA5, isDown: true, isInjected: false, captureEnabled: true));
    }

    [Fact]
    public void KeyboardRouting_ReservesOnlyLocalControlAltDeleteSequence()
    {
        PhysicalKeyboardRoutingState state = new();

        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(0xA2, isDown: true, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(0xA4, isDown: true, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.LocalSecureAttention,
            state.Process(0x2E, isDown: true, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.PassThrough,
            state.Process(0x2E, isDown: false, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.PassThrough,
            state.Process(0xA4, isDown: false, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.PassThrough,
            state.Process(0xA2, isDown: false, isInjected: false, captureEnabled: true));
        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(0x74, isDown: true, isInjected: false, captureEnabled: true));
    }

    [Fact]
    public void KeyboardRouting_ReleasesCaptureWhenWindowIsNotEligible()
    {
        PhysicalKeyboardRoutingState state = new();

        _ = state.Process(0xA2, isDown: true, isInjected: false, captureEnabled: true);
        Assert.Equal(
            PhysicalKeyboardDisposition.PassThrough,
            state.Process(0x41, isDown: true, isInjected: false, captureEnabled: false));
        Assert.Equal(
            PhysicalKeyboardDisposition.ForwardRemote,
            state.Process(0x2E, isDown: true, isInjected: false, captureEnabled: true));
    }

    [Fact]
    public async Task GdiSource_AppliesOnlyCanonicalProfiles()
    {
        await using GdiJpegScreenFrameSource source = new(QualityPreset.Balanced);

        source.ApplyQualityProfile(QualityProfiles.Smooth);

        Assert.Equal(QualityProfiles.Smooth, source.CurrentQualityProfile);
        Assert.Throws<ArgumentException>(() => source.ApplyQualityProfile(
            new QualityProfile(QualityPreset.Smooth, 640, 360, 60, 20)));
    }

    [Fact]
    public async Task SasPipeProtocol_RoundTripsOnlyFixedCommandAndBoundedResult()
    {
        using MemoryStream requestStream = new();
        await SasPipeProtocol.WriteRequestAsync(requestStream);
        requestStream.Position = 0;
        await SasPipeProtocol.ReadAndValidateRequestAsync(requestStream);

        using MemoryStream resultStream = new();
        await SasPipeProtocol.WriteResultAsync(resultStream, true, "service-accepted");
        resultStream.Position = 0;
        (bool succeeded, string message) = await SasPipeProtocol.ReadResultAsync(resultStream);

        Assert.True(succeeded);
        Assert.Equal("service-accepted", message);
    }

    [Fact]
    public async Task SasPipeProtocol_RejectsUnknownCommand()
    {
        byte[] request = [.. SasPipeProtocol.RequestMagic.ToArray(), 99];
        using MemoryStream stream = new(request);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await SasPipeProtocol.ReadAndValidateRequestAsync(stream));
    }
}
