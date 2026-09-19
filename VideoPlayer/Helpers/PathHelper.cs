using System.Runtime.InteropServices;

using System.IO;

namespace VideoPlayer.Helpers;

public static class PathHelper
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public static string CreatePathKey(string path)
    {
        return NormalizePath(path).ToUpperInvariant();
    }

    public static int NaturalCompare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return StrCmpLogicalW(x, y);
    }
}
