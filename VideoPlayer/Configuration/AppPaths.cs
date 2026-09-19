using System.IO;

namespace VideoPlayer.Configuration;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "VideoPlayer.db");

    public string ConfigPath => Path.Combine(RootDirectory, "appsettings.json");

    public string ConfigBackupPath => Path.Combine(RootDirectory, "appsettings.json.bak");

    public string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public string ScreenshotDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "VideoPlayer");

    public static AppPaths CreateDefault()
    {
        return new AppPaths(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VideoPlayer"));
    }

    public void EnsureCoreDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
