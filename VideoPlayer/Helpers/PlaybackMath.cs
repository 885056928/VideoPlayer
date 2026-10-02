using System.IO;

namespace VideoPlayer.Helpers;

public static class PlaybackMath
{
    public static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > duration ? duration : position;
    }

    public static int ClampVolume(int volume) => Math.Clamp(volume, 0, 100);

    public static double ClampRate(double rate)
        => double.IsFinite(rate) ? Math.Clamp(rate, 0.25, 5.0) : 1.0;
}
