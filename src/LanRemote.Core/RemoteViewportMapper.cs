namespace LanRemote.Core;

public enum RemoteScaleMode
{
    Fit,
    Stretch,
    Crop,
}

public static class RemoteViewportMapper
{
    public static bool TryNormalizePoint(
        RemoteScaleMode mode,
        double contentWidth,
        double contentHeight,
        double viewportWidth,
        double viewportHeight,
        double pointX,
        double pointY,
        out float normalizedX,
        out float normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        if (contentWidth <= 0 || contentHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0 ||
            !double.IsFinite(pointX) || !double.IsFinite(pointY))
        {
            return false;
        }

        if (pointX < 0 || pointY < 0 || pointX > viewportWidth || pointY > viewportHeight)
        {
            return false;
        }

        if (mode == RemoteScaleMode.Stretch)
        {
            normalizedX = (float)Math.Clamp(pointX / viewportWidth, 0, 1);
            normalizedY = (float)Math.Clamp(pointY / viewportHeight, 0, 1);
            return true;
        }

        double scale = mode switch
        {
            RemoteScaleMode.Fit => Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight),
            RemoteScaleMode.Crop => Math.Max(viewportWidth / contentWidth, viewportHeight / contentHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        double renderedWidth = contentWidth * scale;
        double renderedHeight = contentHeight * scale;
        double offsetX = (viewportWidth - renderedWidth) / 2d;
        double offsetY = (viewportHeight - renderedHeight) / 2d;

        double x = (pointX - offsetX) / renderedWidth;
        double y = (pointY - offsetY) / renderedHeight;
        if (mode == RemoteScaleMode.Fit && (x < 0 || x > 1 || y < 0 || y > 1))
        {
            return false;
        }

        normalizedX = (float)Math.Clamp(x, 0, 1);
        normalizedY = (float)Math.Clamp(y, 0, 1);
        return true;
    }
}
