using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class PlaybackMathTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 5)]
    [InlineData(20, 10)]
    public void ClampPosition_UsesInclusiveRange(int input, int expected)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expected),
            PlaybackMath.ClampPosition(TimeSpan.FromSeconds(input), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ClampVolume_UsesZeroToOneHundred()
        => Assert.Equal(100, PlaybackMath.ClampVolume(500));

    [Fact]
    public void ClampRate_UsesHalfToDouble()
        => Assert.Equal(2.0, PlaybackMath.ClampRate(9.0));
}
