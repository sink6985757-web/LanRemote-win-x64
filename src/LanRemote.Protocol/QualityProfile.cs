using System.Text.Json.Serialization;

namespace LanRemote.Protocol;

public enum QualityPreset : byte
{
    Smooth = 1,
    Balanced = 2,
    Quality = 3,
}

public sealed record QualityProfile(
    QualityPreset Preset,
    int MaximumWidth,
    int MaximumHeight,
    int FramesPerSecond,
    int JpegQuality)
{
    [JsonIgnore]
    public string DisplayName => Preset switch
    {
        QualityPreset.Smooth => "流暢",
        QualityPreset.Balanced => "平衡",
        QualityPreset.Quality => "畫質",
        _ => Preset.ToString(),
    };
}

public static class QualityProfiles
{
    public static QualityProfile Smooth { get; } = new(
        QualityPreset.Smooth,
        1280,
        720,
        30,
        40);

    public static QualityProfile Balanced { get; } = new(
        QualityPreset.Balanced,
        1600,
        900,
        24,
        60);

    public static QualityProfile Quality { get; } = new(
        QualityPreset.Quality,
        1920,
        1080,
        15,
        82);

    public static QualityProfile Get(QualityPreset preset) => preset switch
    {
        QualityPreset.Smooth => Smooth,
        QualityPreset.Balanced => Balanced,
        QualityPreset.Quality => Quality,
        _ => throw new ProtocolException($"Unsupported quality preset {(byte)preset}."),
    };

    public static bool IsCanonical(QualityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile == Get(profile.Preset);
    }
}
