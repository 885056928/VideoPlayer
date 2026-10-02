using System.IO;
using VideoPlayer.Helpers;
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
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        settings.Player ??= new PlayerSettings();
        settings.Subtitle ??= new SubtitleSettings();
        settings.UI ??= new UiSettings();
        settings.Screenshot ??= new ScreenshotSettings();

        settings.Player.DefaultVolume = Math.Clamp(settings.Player.DefaultVolume, 0, 100);
        // 配置与播放服务使用同一倍率范围，避免重启后已保存的倍率被重新截断。
        settings.Player.DefaultPlaybackRate = PlaybackMath.ClampRate(settings.Player.DefaultPlaybackRate);
        settings.Subtitle.Encoding = AllowedEncodings.Contains(settings.Subtitle.Encoding)
            ? settings.Subtitle.Encoding
            : "Auto";
        settings.Screenshot.Directory = string.IsNullOrWhiteSpace(settings.Screenshot.Directory)
            ? string.Empty
            : Path.GetFullPath(settings.Screenshot.Directory);

        return settings;
    }
}
