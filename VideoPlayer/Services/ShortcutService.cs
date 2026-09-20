using System.Windows.Input;

namespace VideoPlayer.Services;

public enum ShortcutAction
{
    None,
    TogglePlayPause,
    SeekBackward5,
    SeekForward5,
    SeekBackward30,
    SeekForward30,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    RateDown,
    RateUp,
    RateReset,
    ToggleFullscreen,
    ExitFullscreen,
    OpenFile,
    OpenFolder,
    TogglePlaylist,
    VideoInfo,
    Screenshot
}

public static class ShortcutService
{
    public static ShortcutAction Resolve(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F11) return ShortcutAction.ToggleFullscreen;
        if (key == Key.Escape) return ShortcutAction.ExitFullscreen;
        if (key == Key.Space) return ShortcutAction.TogglePlayPause;
        if (key == Key.Left) return modifiers.HasFlag(ModifierKeys.Shift)
            ? ShortcutAction.SeekBackward30
            : ShortcutAction.SeekBackward5;
        if (key == Key.Right) return modifiers.HasFlag(ModifierKeys.Shift)
            ? ShortcutAction.SeekForward30
            : ShortcutAction.SeekForward5;
        if (key == Key.Up) return ShortcutAction.VolumeUp;
        if (key == Key.Down) return ShortcutAction.VolumeDown;
        if (key == Key.M) return ShortcutAction.ToggleMute;
        if (key == Key.O && modifiers.HasFlag(ModifierKeys.Control))
            return modifiers.HasFlag(ModifierKeys.Shift)
                ? ShortcutAction.OpenFolder
                : ShortcutAction.OpenFile;
        if (key == Key.P && modifiers.HasFlag(ModifierKeys.Control))
            return ShortcutAction.TogglePlaylist;
        if (key == Key.OemOpenBrackets) return ShortcutAction.RateDown;
        if (key == Key.OemCloseBrackets) return ShortcutAction.RateUp;
        if (key == Key.OemPipe) return ShortcutAction.RateReset;
        if (key == Key.I && modifiers.HasFlag(ModifierKeys.Control)) return ShortcutAction.VideoInfo;
        if (key == Key.S && modifiers.HasFlag(ModifierKeys.Control) &&
            modifiers.HasFlag(ModifierKeys.Alt)) return ShortcutAction.Screenshot;
        return ShortcutAction.None;
    }
}
