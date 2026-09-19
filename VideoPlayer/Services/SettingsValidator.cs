using System.IO;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public static class SettingsValidator
{
    private static readonly HashSet<string> AllowedEncodings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Auto", "UTF-8", "GB18030", "UTF-16"
        };

    public static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= AppSettings.CreateDefault();
        settings.SchemaVersion = 1;
        settings.Player ??= new PlayerSettings();
        settings.Subtitle ??= new SubtitleSettings();
        settings.UI ??= new UiSettings();
        settings.Screenshot ??= new ScreenshotSettings();

        settings.Player.DefaultVolume = Math.Clamp(settings.Player.DefaultVolume, 0, 100);
        settings.Player.DefaultPlaybackRate = Math.Clamp(settings.Player.DefaultPlaybackRate, 0.5, 2.0);
        settings.Subtitle.Encoding = AllowedEncodings.Contains(settings.Subtitle.Encoding)
            ? settings.Subtitle.Encoding
            : "Auto";
        settings.Screenshot.Directory = string.IsNullOrWhiteSpace(settings.Screenshot.Directory)
            ? string.Empty
            : Path.GetFullPath(settings.Screenshot.Directory);

        return settings;
    }
}
