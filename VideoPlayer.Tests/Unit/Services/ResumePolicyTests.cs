using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ResumePolicyTests
{
    [Fact]
    public void ShouldAsk_AtMidpoint_ReturnsTrue()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            false, DateTimeOffset.UtcNow, 1);

        Assert.True(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenCompleted_ReturnsFalse()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            true, DateTimeOffset.UtcNow, 1);

        Assert.False(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenOnlyFiveSecondsIn_ReturnsFalse()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5),
            false, DateTimeOffset.UtcNow, 1);

        Assert.False(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenDurationEqualsThirtySeconds_ReturnsTrue()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15),
            false, DateTimeOffset.UtcNow, 1);

        Assert.True(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenLastPositionEqualsFifteenSeconds_ReturnsTrue()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15),
            false, DateTimeOffset.UtcNow, 1);

        Assert.True(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenLastPositionEqualsNinetyFivePercent_ReturnsFalse()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromSeconds(1000), TimeSpan.FromSeconds(950),
            false, DateTimeOffset.UtcNow, 1);

        Assert.False(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenRemainingEqualsTenSeconds_ReturnsTrue()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(90),
            false, DateTimeOffset.UtcNow, 1);

        Assert.True(ResumePolicy.ShouldAsk(info));
    }}
