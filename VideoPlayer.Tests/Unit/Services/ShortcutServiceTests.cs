using System.Windows.Input;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ShortcutServiceTests
{
    [Theory]
    [InlineData(Key.Space, ModifierKeys.None, ShortcutAction.TogglePlayPause)]
    [InlineData(Key.Left, ModifierKeys.None, ShortcutAction.SeekBackward5)]
    [InlineData(Key.Right, ModifierKeys.Shift, ShortcutAction.SeekForward30)]
    [InlineData(Key.F11, ModifierKeys.None, ShortcutAction.ToggleFullscreen)]
    [InlineData(Key.Escape, ModifierKeys.None, ShortcutAction.ExitFullscreen)]
    [InlineData(Key.M, ModifierKeys.None, ShortcutAction.ToggleMute)]
    [InlineData(Key.OemOpenBrackets, ModifierKeys.None, ShortcutAction.RateDown)]
    [InlineData(Key.OemCloseBrackets, ModifierKeys.None, ShortcutAction.RateUp)]
    [InlineData(Key.OemPipe, ModifierKeys.None, ShortcutAction.RateReset)]
    [InlineData(Key.I, ModifierKeys.Control, ShortcutAction.VideoInfo)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt, ShortcutAction.Screenshot)]
    public void Resolve_MapsRequiredShortcut(
        Key key,
        ModifierKeys modifiers,
        ShortcutAction expected)
    {
        Assert.Equal(expected, ShortcutService.Resolve(key, modifiers));
    }

    [Fact]
    public void Resolve_ReturnsNoneForUnmappedKey()
    {
        Assert.Equal(ShortcutAction.None, ShortcutService.Resolve(Key.A, ModifierKeys.None));
    }
}
