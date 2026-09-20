using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoPlayer.Models;

public sealed partial class PlaylistItem : ObservableObject
{
    public required string FilePath { get; init; }

    public string FileName => Path.GetFileName(FilePath);

    [ObservableProperty]
    private TimeSpan? _duration;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isPlaying;
}