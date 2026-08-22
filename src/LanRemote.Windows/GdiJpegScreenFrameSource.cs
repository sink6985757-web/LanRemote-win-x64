using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using LanRemote.Core;
using LanRemote.Protocol;
using Screen = System.Windows.Forms.Screen;

namespace LanRemote.Windows;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class GdiJpegScreenFrameSource : IScreenFrameSource
{
    private readonly ImageCodecInfo _jpegEncoder;
    private QualityProfile _qualityProfile;

    public GdiJpegScreenFrameSource(QualityPreset initialPreset = QualityPreset.Balanced)
    {
        _qualityProfile = QualityProfiles.Get(initialPreset);
        _jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public VideoCodec Codec => VideoCodec.Jpeg;

    public QualityProfile CurrentQualityProfile => Volatile.Read(ref _qualityProfile);

    public void ApplyQualityProfile(QualityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!QualityProfiles.IsCanonical(profile))
        {
            throw new ArgumentException("Only canonical LanRemote quality profiles are allowed.", nameof(profile));
        }

        Volatile.Write(ref _qualityProfile, profile);
    }

    public async IAsyncEnumerable<VideoFramePayload> CaptureAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            QualityProfile profile = CurrentQualityProfile;
            TimeSpan frameInterval = TimeSpan.FromSeconds(1d / profile.FramesPerSecond);
            Stopwatch timer = Stopwatch.StartNew();
            yield return CaptureFrame(profile);
            timer.Stop();
            TimeSpan delay = frameInterval - timer.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    private VideoFramePayload CaptureFrame(QualityProfile profile)
    {
        Screen primary = Screen.PrimaryScreen ?? throw new InvalidOperationException("找不到主要螢幕。");
        Rectangle bounds = primary.Bounds;
        using Bitmap native = new(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(native))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        Size outputSize = FitInside(
            bounds.Size,
            new Size(profile.MaximumWidth, profile.MaximumHeight));
        using Bitmap output = outputSize == bounds.Size
            ? (Bitmap)native.Clone()
            : Resize(native, outputSize);
        using MemoryStream stream = new();
        using EncoderParameters encoderParameters = new(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, profile.JpegQuality);
        output.Save(stream, _jpegEncoder, encoderParameters);
        return new VideoFramePayload(
            output.Width,
            output.Height,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            VideoCodec.Jpeg,
            stream.ToArray());
    }

    private static Bitmap Resize(Bitmap source, Size outputSize)
    {
        Bitmap result = new(outputSize.Width, outputSize.Height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.DrawImage(source, new Rectangle(Point.Empty, outputSize));
        return result;
    }

    private static Size FitInside(Size source, Size maximum)
    {
        double ratio = Math.Min(1d, Math.Min(
            (double)maximum.Width / source.Width,
            (double)maximum.Height / source.Height));
        return new Size(
            Math.Max(1, (int)Math.Round(source.Width * ratio)),
            Math.Max(1, (int)Math.Round(source.Height * ratio)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
