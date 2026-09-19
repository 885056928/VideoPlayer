using System.IO;

namespace VideoPlayer.Models;

public sealed class PlaylistItem
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public TimeSpan? Duration { get; set; }
}
