using System.IO;

namespace VideoPlayer.Helpers;

public static class FileNameHelper
{
    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "screenshot" : sanitized;
    }
}
