using System.Windows.Input;
using VideoPlayer;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ShortcutServiceTests
{
    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, ShortcutAction.TogglePlayPause)]
    [InlineData(Key.Left, ModifierKeys.None, ShortcutAction.SeekBackward5)]
    [InlineData(Key.Left, ModifierKeys.Shift, ShortcutAction.SeekBackward30)]
    [InlineData(Key.Right, ModifierKeys.None, ShortcutAction.SeekForward5)]
    [InlineData(Key.Right, ModifierKeys.Shift, ShortcutAction.SeekForward30)]
    [InlineData(Key.Up, ModifierKeys.None, ShortcutAction.VolumeUp)]
    [InlineData(Key.Down, ModifierKeys.None, ShortcutAction.VolumeDown)]
    [InlineData(Key.M, ModifierKeys.None, ShortcutAction.ToggleMute)]
    [InlineData(Key.O, ModifierKeys.Control, ShortcutAction.OpenFile)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.OpenFolder)]
    [InlineData(Key.P, ModifierKeys.Control, ShortcutAction.TogglePlaylist)]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.None, ShortcutAction.RateDown)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.None, ShortcutAction.RateUp)]
    [InlineData(Key.OemPipe, ModifierKeys.None, ShortcutAction.RateReset)]
    [InlineData(Key.I, ModifierKeys.Control, ShortcutAction.VideoInfo)]
    [InlineData(Key.F11, ModifierKeys.None, ShortcutAction.ToggleFullscreen)]
    [InlineData(Key.Escape, ModifierKeys.None, ShortcutAction.ExitFullscreen)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt, ShortcutAction.Screenshot)]
    public void Resolve_MapsRequiredShortcut(
        Key key,
        ModifierKeys modifiers,
        ShortcutAction expected)
    {
        Assert.Equal(expected, ShortcutService.Resolve(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.Alt)]
    [InlineData(Key.Space, ModifierKeys.Control)]
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Left, ModifierKeys.Alt)]
    [InlineData(Key.Right, ModifierKeys.Alt)]
    [InlineData(Key.Up, ModifierKeys.Control)]
    [InlineData(Key.Down, ModifierKeys.Shift)]
    [InlineData(Key.M, ModifierKeys.Control)]
    [InlineData(Key.F11, ModifierKeys.Alt)]
    [InlineData(Key.Escape, ModifierKeys.Alt)]
    [InlineData(Key.O, ModifierKeys.Alt)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData(Key.P, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.P, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData(Key.I, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.S, ModifierKeys.Control)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.Control)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.Alt)]
    [InlineData(Key.OemPipe, ModifierKeys.Shift)]
    public void Resolve_ReturnsNoneForConflictingModifiers(
        Key key,
        ModifierKeys modifiers)
    {
        Assert.Equal(ShortcutAction.None, ShortcutService.Resolve(key, modifiers));
    }

    [Theory]
    [InlineData(ShortcutAction.None, false, false)]
    [InlineData(ShortcutAction.ExitFullscreen, false, false)]
    [InlineData(ShortcutAction.ExitFullscreen, true, true)]
    [InlineData(ShortcutAction.TogglePlayPause, false, true)]
    [InlineData(ShortcutAction.ToggleFullscreen, false, true)]
    public void ShouldHandleShortcut_RequiresActionAndFullscreenForEscape(
        ShortcutAction action,
        bool isFullscreen,
        bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldHandleShortcut(action, isFullscreen));
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.Alt)]
    [InlineData(Key.Space, ModifierKeys.Control)]
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Left, ModifierKeys.Alt)]
    [InlineData(Key.M, ModifierKeys.Control)]
    public void ConflictingShortcut_IsNotHandled(
        Key key,
        ModifierKeys modifiers)
    {
        var action = ShortcutService.Resolve(key, modifiers);

        Assert.False(MainWindow.ShouldHandleShortcut(action, isFullscreen: false));
    }

    [Fact]
    public void Escape_WhenWindowed_IsNotHandled()
    {
        var action = ShortcutService.Resolve(Key.Escape, ModifierKeys.None);

        Assert.False(MainWindow.ShouldHandleShortcut(action, isFullscreen: false));
    }

    [Fact]
    public void Resolve_ReturnsNoneForUnmappedKey()
    {
        Assert.Equal(ShortcutAction.None, ShortcutService.Resolve(Key.A, ModifierKeys.None));
    }
}
