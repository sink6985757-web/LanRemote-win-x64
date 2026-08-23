using LanRemote.Protocol;
using LanRemote.Windows;

namespace LanRemote.Tests;

public sealed class WindowsCaptureTests
{
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
