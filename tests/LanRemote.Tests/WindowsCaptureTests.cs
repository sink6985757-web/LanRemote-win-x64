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
}
