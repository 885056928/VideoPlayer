using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaybackStateMachineTests
{
    [Theory]
    [InlineData(PlaybackState.None, PlaybackState.Opening)]
    [InlineData(PlaybackState.Opening, PlaybackState.Playing)]
    [InlineData(PlaybackState.Playing, PlaybackState.Paused)]
    [InlineData(PlaybackState.Paused, PlaybackState.Playing)]
    [InlineData(PlaybackState.Playing, PlaybackState.Ended)]
    [InlineData(PlaybackState.Error, PlaybackState.Opening)]
    public void TryTransition_AllowsDocumentedTransitions(
        PlaybackState current,
        PlaybackState next)
    {
        var machine = new PlaybackStateMachine(current);

        var accepted = machine.TryTransition(next, out var reason);

        Assert.True(accepted);
        Assert.Null(reason);
        Assert.Equal(next, machine.Current);
    }

    [Fact]
    public void TryTransition_RejectsEndedToPlaying()
    {
        var machine = new PlaybackStateMachine(PlaybackState.Ended);

        var accepted = machine.TryTransition(PlaybackState.Playing, out var reason);

        Assert.False(accepted);
        Assert.Equal(PlaybackState.Ended, machine.Current);
        Assert.Contains("Ended", reason);
    }
}
