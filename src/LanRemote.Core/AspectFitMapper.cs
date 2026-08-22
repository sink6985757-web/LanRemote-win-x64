namespace LanRemote.Core;

public static class AspectFitMapper
{
    public static bool TryNormalizePoint(
        int contentWidth,
        int contentHeight,
        double viewportWidth,
        double viewportHeight,
        double pointX,
        double pointY,
        out float normalizedX,
        out float normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;
        if (contentWidth <= 0 || contentHeight <= 0 ||
            viewportWidth <= 0 || viewportHeight <= 0 ||
            !double.IsFinite(pointX) || !double.IsFinite(pointY))
        {
            return false;
        }

        double contentRatio = (double)contentWidth / contentHeight;
        double viewportRatio = viewportWidth / viewportHeight;
        double renderedWidth;
        double renderedHeight;
        double offsetX;
        double offsetY;
        if (viewportRatio > contentRatio)
        {
            renderedHeight = viewportHeight;
            renderedWidth = renderedHeight * contentRatio;
            offsetX = (viewportWidth - renderedWidth) / 2d;
            offsetY = 0;
        }
        else
        {
            renderedWidth = viewportWidth;
            renderedHeight = renderedWidth / contentRatio;
            offsetX = 0;
            offsetY = (viewportHeight - renderedHeight) / 2d;
        }

        double x = (pointX - offsetX) / renderedWidth;
        double y = (pointY - offsetY) / renderedHeight;
        if (x is < 0 or > 1 || y is < 0 or > 1)
        {
            return false;
        }

        normalizedX = (float)x;
        normalizedY = (float)y;
        return true;
    }
}
