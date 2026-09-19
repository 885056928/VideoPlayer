using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record PlayerTrackInfo(
    PlayerTrackType Type,
    int Id,
    string Name,
    string Language,
    bool IsSelected,
    bool IsExternal);
