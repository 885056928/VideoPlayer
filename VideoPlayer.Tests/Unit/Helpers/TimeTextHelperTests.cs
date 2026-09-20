using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class TimeTextHelperTests
{
    [Fact]
    public void Format_ClampsNegativeTimeToZero()
    {
        Assert.Equal("00:00:00", TimeTextHelper.Format(TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [InlineData(0, 0, 0, 123, "00:00:00")]
    [InlineData(0, 2, 3, 456, "00:02:03")]
    [InlineData(25, 1, 2, 999, "25:01:02")]
    public void Format_DropsFractionsAndUsesHoursMinutesSeconds(
        int hours,
        int minutes,
        int seconds,
        int milliseconds,
        string expected)
    {
        var value = new TimeSpan(0, hours, minutes, seconds, milliseconds);

        Assert.Equal(expected, TimeTextHelper.Format(value));
    }
}