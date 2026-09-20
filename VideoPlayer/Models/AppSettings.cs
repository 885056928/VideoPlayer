namespace VideoPlayer.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public PlayerSettings Player { get; set; } = new();

    public SubtitleSettings Subtitle { get; set; } = new();

    public UiSettings UI { get; set; } = new();

    public ScreenshotSettings Screenshot { get; set; } = new();

    public static AppSettings CreateDefault() => new();
}

public sealed class PlayerSettings
{
    public int DefaultVolume { get; set; } = 80;

    public double DefaultPlaybackRate { get; set; } = 1.0;

    public bool AutoPlayNext { get; set; } = true;

    public bool RememberPosition { get; set; } = true;
}

public sealed class SubtitleSettings
{
    public bool AutoLoad { get; set; } = true;

    public string Encoding { get; set; } = "Auto";
}

public sealed class UiSettings
{
    public string Theme { get; set; } = "Dark";

    public bool ShowPlaylist { get; set; }
}

public sealed class ScreenshotSettings
{
    public string Directory { get; set; } = string.Empty;
}
