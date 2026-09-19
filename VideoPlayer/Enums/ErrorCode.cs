namespace VideoPlayer.Enums;

public enum ErrorCode
{
    None,
    InvalidPath,
    FileNotFound,
    NoPermission,
    UnsupportedMedia,
    PlayerNotInitialized,
    InvalidState,
    SeekNotAvailable,
    SubtitleNotFound,
    TrackNotFound,
    DatabaseUnavailable,
    ConfigInvalid,
    SnapshotFailed,
    Superseded,
    Cancelled,
    Unknown
}
