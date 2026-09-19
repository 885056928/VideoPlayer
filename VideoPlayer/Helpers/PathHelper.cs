using System.IO;

namespace VideoPlayer.Helpers;

public static class PathHelper
{
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public static string CreatePathKey(string path)
    {
        return NormalizePath(path).ToUpperInvariant();
    }
}