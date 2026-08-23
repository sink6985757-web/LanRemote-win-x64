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
        return RemoteViewportMapper.TryNormalizePoint(
            RemoteScaleMode.Fit,
            contentWidth,
            contentHeight,
            viewportWidth,
            viewportHeight,
            pointX,
            pointY,
            out normalizedX,
            out normalizedY);
    }
}
