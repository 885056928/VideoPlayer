using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Normalize_ClampsInvalidValues()
    {
        var settings = AppSettings.CreateDefault();
        settings.Player.DefaultVolume = 500;
        settings.Player.DefaultPlaybackRate = 9.0;
        settings.Subtitle.Encoding = "GBK";

        var actual = SettingsValidator.Normalize(settings);

        Assert.Equal(100, actual.Player.DefaultVolume);
        Assert.Equal(5.0, actual.Player.DefaultPlaybackRate);
        Assert.Equal("Auto", actual.Subtitle.Encoding);
    }

    [Theory]
    [InlineData(0.1, 0.25)]
    [InlineData(0.25, 0.25)]
    [InlineData(3.5, 3.5)]
    [InlineData(5.0, 5.0)]
    [InlineData(9.0, 5.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 1.0)]
    public void Normalize_PlaybackRateUsesExtendedRange(double input, double expected)
    {
        var settings = AppSettings.CreateDefault();
        settings.Player.DefaultPlaybackRate = input;

        var actual = SettingsValidator.Normalize(settings);

        Assert.Equal(expected, actual.Player.DefaultPlaybackRate);
    }

    [Fact]
    public void Normalize_RepairsNullNestedSections()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 0,
            Player = null!,
            Subtitle = null!,
            UI = null!,
            Screenshot = null!
        };

        var actual = SettingsValidator.Normalize(settings);

        Assert.Equal(1, actual.SchemaVersion);
        Assert.NotNull(actual.Player);
        Assert.NotNull(actual.Subtitle);
        Assert.NotNull(actual.UI);
        Assert.NotNull(actual.Screenshot);
    }
}
