namespace VideoPlayer.Helpers;

public static class SeekHoverMath
{
    public static TimeSpan PositionFromRatio(double x, double width, TimeSpan duration)
    {
        if (width <= 0 || duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var ratio = Math.Clamp(x / width, 0, 1);
        return TimeSpan.FromTicks((long)(duration.Ticks * ratio));
    }

    public static TimeSpan QuantizeForPreview(TimeSpan position, TimeSpan duration)
    {
        var milliseconds = Math.Round(
            position.TotalMilliseconds / 200d,
            MidpointRounding.AwayFromZero) * 200d;
        var quantized = TimeSpan.FromMilliseconds(milliseconds);
        return quantized > duration ? duration : quantized;
    }
}