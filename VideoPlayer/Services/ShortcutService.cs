using System.Windows.Input;

namespace VideoPlayer.Services;

public enum ShortcutAction
{
    None,
    TogglePlayPause,
    Previous,
    Next,
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
        return key switch
        {
            Key.F11 when modifiers == ModifierKeys.None => ShortcutAction.ToggleFullscreen,
            Key.Escape when modifiers == ModifierKeys.None => ShortcutAction.ExitFullscreen,
            Key.Space when modifiers == ModifierKeys.None => ShortcutAction.TogglePlayPause,
            Key.Left when modifiers == ModifierKeys.Control => ShortcutAction.Previous,
            Key.Right when modifiers == ModifierKeys.Control => ShortcutAction.Next,
            Key.Left when modifiers == ModifierKeys.None => ShortcutAction.SeekBackward5,
            Key.Left when modifiers == ModifierKeys.Shift => ShortcutAction.SeekBackward30,
            Key.Right when modifiers == ModifierKeys.None => ShortcutAction.SeekForward5,
            Key.Right when modifiers == ModifierKeys.Shift => ShortcutAction.SeekForward30,
            Key.Up when modifiers == ModifierKeys.None => ShortcutAction.VolumeUp,
            Key.Down when modifiers == ModifierKeys.None => ShortcutAction.VolumeDown,
            Key.M when modifiers == ModifierKeys.None => ShortcutAction.ToggleMute,
            Key.O when modifiers == ModifierKeys.Control => ShortcutAction.OpenFile,
            Key.O when modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
                => ShortcutAction.OpenFolder,
            Key.P when modifiers == ModifierKeys.Control => ShortcutAction.TogglePlaylist,
            Key.OemOpenBrackets when modifiers == ModifierKeys.None => ShortcutAction.RateDown,
            Key.OemCloseBrackets when modifiers == ModifierKeys.None => ShortcutAction.RateUp,
            Key.OemPipe when modifiers == ModifierKeys.None => ShortcutAction.RateReset,
            Key.I when modifiers == ModifierKeys.Control => ShortcutAction.VideoInfo,
            Key.S when modifiers == (ModifierKeys.Control | ModifierKeys.Alt)
                => ShortcutAction.Screenshot,
            _ => ShortcutAction.None
        };
    }
}
