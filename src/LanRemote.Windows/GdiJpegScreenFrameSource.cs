using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
            DrawNativeCursor(graphics, bounds);
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

    private static void DrawNativeCursor(Graphics graphics, Rectangle screenBounds)
    {
        CursorInfo cursorInfo = new()
        {
            Size = Marshal.SizeOf<CursorInfo>(),
        };
        if (!GetCursorInfo(ref cursorInfo) || cursorInfo.Flags != CursorShowing || cursorInfo.Cursor == nint.Zero)
        {
            return;
        }

        nint icon = CopyIcon(cursorInfo.Cursor);
        if (icon == nint.Zero)
        {
            return;
        }

        IconInfo iconInfo = default;
        try
        {
            if (!GetIconInfo(icon, out iconInfo))
            {
                return;
            }

            int x = cursorInfo.Position.X - screenBounds.Left - checked((int)iconInfo.XHotspot);
            int y = cursorInfo.Position.Y - screenBounds.Top - checked((int)iconInfo.YHotspot);
            nint deviceContext = graphics.GetHdc();
            try
            {
                _ = DrawIconEx(deviceContext, x, y, icon, 0, 0, 0, nint.Zero, DrawIconNormal);
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }
        finally
        {
            if (iconInfo.MaskBitmap != nint.Zero)
            {
                _ = DeleteObject(iconInfo.MaskBitmap);
            }

            if (iconInfo.ColorBitmap != nint.Zero)
            {
                _ = DeleteObject(iconInfo.ColorBitmap);
            }

            _ = DestroyIcon(icon);
        }
    }

    private const int CursorShowing = 1;
    private const uint DrawIconNormal = 3;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll")]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(nint icon, out IconInfo iconInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        nint deviceContext,
        int x,
        int y,
        nint icon,
        int width,
        int height,
        uint animationStep,
        nint flickerFreeBrush,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public nint Cursor;
        public Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public nint MaskBitmap;
        public nint ColorBitmap;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
