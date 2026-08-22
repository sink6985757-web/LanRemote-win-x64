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
    private readonly int _framesPerSecond;
    private readonly int _maximumWidth;
    private readonly int _maximumHeight;
    private readonly long _jpegQuality;
    private readonly ImageCodecInfo _jpegEncoder;

    public GdiJpegScreenFrameSource(
        int framesPerSecond = 30,
        int maximumWidth = 1920,
        int maximumHeight = 1080,
        long jpegQuality = 58)
    {
        if (framesPerSecond is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        if (maximumWidth < 320 || maximumHeight < 240)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        if (jpegQuality is < 20 or > 95)
        {
            throw new ArgumentOutOfRangeException(nameof(jpegQuality));
        }

        _framesPerSecond = framesPerSecond;
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
        _jpegQuality = jpegQuality;
        _jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    public VideoCodec Codec => VideoCodec.Jpeg;

    public async IAsyncEnumerable<VideoFramePayload> CaptureAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TimeSpan frameInterval = TimeSpan.FromSeconds(1d / _framesPerSecond);
        while (!cancellationToken.IsCancellationRequested)
        {
            Stopwatch timer = Stopwatch.StartNew();
            yield return CaptureFrame();
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

    private VideoFramePayload CaptureFrame()
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

        Size outputSize = FitInside(bounds.Size, new Size(_maximumWidth, _maximumHeight));
        using Bitmap output = outputSize == bounds.Size
            ? (Bitmap)native.Clone()
            : Resize(native, outputSize);
        using MemoryStream stream = new();
        using EncoderParameters encoderParameters = new(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, _jpegQuality);
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
