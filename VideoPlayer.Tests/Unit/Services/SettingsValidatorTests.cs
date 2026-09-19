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
        Assert.Equal(2.0, actual.Player.DefaultPlaybackRate);
        Assert.Equal("Auto", actual.Subtitle.Encoding);
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
