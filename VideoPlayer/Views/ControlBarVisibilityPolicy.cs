namespace VideoPlayer.Views;

public static class ControlBarVisibilityPolicy
{
    public static bool ShouldStartAutoHideTimer(bool isLoaded, bool isFullscreen)
        => isLoaded && isFullscreen;

    public static bool ShouldCollapseOnIdle(bool isFullscreen)
        => isFullscreen;
}