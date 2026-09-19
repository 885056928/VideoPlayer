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

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void ClampVolume_UsesZeroToOneHundred(int input, int expected)
        => Assert.Equal(expected, PlaybackMath.ClampVolume(input));

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.25, 1.25)]
    [InlineData(2.0, 2.0)]
    [InlineData(9.0, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void ClampRate_UsesHalfToDoubleAndFiniteDefault(double input, double expected)
        => Assert.Equal(expected, PlaybackMath.ClampRate(input));
}
