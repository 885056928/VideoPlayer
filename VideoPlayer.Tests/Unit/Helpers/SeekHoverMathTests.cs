using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class SeekHoverMathTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(50, 5)]
    [InlineData(150, 10)]
    public void PositionFromRatio_ClampsToDuration(double x, double expectedSeconds)
    {
        var actual = SeekHoverMath.PositionFromRatio(
            x,
            100,
            TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [Theory]
    [InlineData(5.24, 5.0)]
    [InlineData(5.25, 5.5)]
    public void QuantizeForPreview_UsesHalfSecondSteps(double input, double expectedSeconds)
    {
        var actual = SeekHoverMath.QuantizeForPreview(
            TimeSpan.FromSeconds(input),
            TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 0)]
    public void PositionFromRatio_ReturnsZeroForInvalidGeometry(double width, int durationSeconds)
    {
        var actual = SeekHoverMath.PositionFromRatio(
            50,
            width,
            TimeSpan.FromSeconds(durationSeconds));

        Assert.Equal(TimeSpan.Zero, actual);
    }
    [Fact]
    public void QuantizeForPreview_DoesNotExceedDuration()
    {
        var duration = TimeSpan.FromSeconds(5);

        var actual = SeekHoverMath.QuantizeForPreview(
            TimeSpan.FromSeconds(5.4),
            duration);

        Assert.Equal(duration, actual);
    }
}