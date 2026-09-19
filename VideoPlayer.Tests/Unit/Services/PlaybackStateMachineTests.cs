using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaybackStateMachineTests
{
    [Theory]
    [InlineData(PlaybackState.None, PlaybackState.Opening)]
    [InlineData(PlaybackState.Opening, PlaybackState.Playing)]
    [InlineData(PlaybackState.Opening, PlaybackState.Stopped)]
    [InlineData(PlaybackState.Opening, PlaybackState.Error)]
    [InlineData(PlaybackState.Playing, PlaybackState.Paused)]
    [InlineData(PlaybackState.Playing, PlaybackState.Stopped)]
    [InlineData(PlaybackState.Playing, PlaybackState.Ended)]
    [InlineData(PlaybackState.Playing, PlaybackState.Error)]
    [InlineData(PlaybackState.Paused, PlaybackState.Playing)]
    [InlineData(PlaybackState.Paused, PlaybackState.Stopped)]
    [InlineData(PlaybackState.Paused, PlaybackState.Error)]
    [InlineData(PlaybackState.Stopped, PlaybackState.Opening)]
    [InlineData(PlaybackState.Ended, PlaybackState.Opening)]
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
    public async Task TryTransition_ConcurrentTransitionsCommitAtMostOnce()
    {
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var machine = new PlaybackStateMachine(PlaybackState.Opening);
            using var barrier = new Barrier(2);

            var stopped = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return machine.TryTransition(PlaybackState.Stopped, out _);
            });
            var error = Task.Run(() =>
            {
                barrier.SignalAndWait();
                return machine.TryTransition(PlaybackState.Error, out _);
            });

            var results = await Task.WhenAll(stopped, error);

            Assert.True(
                results.Count(result => result) == 1,
                $"Iteration {iteration}: stopped={results[0]}, error={results[1]}, current={machine.Current}");
        }
    }
    [Theory]
    [InlineData(PlaybackState.None, PlaybackState.None, "Transition None -> None is not allowed.")]
    [InlineData(PlaybackState.None, PlaybackState.Playing, "Transition None -> Playing is not allowed.")]
    [InlineData(PlaybackState.None, PlaybackState.Paused, "Transition None -> Paused is not allowed.")]
    [InlineData(PlaybackState.None, PlaybackState.Stopped, "Transition None -> Stopped is not allowed.")]
    [InlineData(PlaybackState.None, PlaybackState.Ended, "Transition None -> Ended is not allowed.")]
    [InlineData(PlaybackState.None, PlaybackState.Error, "Transition None -> Error is not allowed.")]
    [InlineData(PlaybackState.Opening, PlaybackState.None, "Transition Opening -> None is not allowed.")]
    [InlineData(PlaybackState.Opening, PlaybackState.Opening, "Transition Opening -> Opening is not allowed.")]
    [InlineData(PlaybackState.Opening, PlaybackState.Paused, "Transition Opening -> Paused is not allowed.")]
    [InlineData(PlaybackState.Opening, PlaybackState.Ended, "Transition Opening -> Ended is not allowed.")]
    [InlineData(PlaybackState.Playing, PlaybackState.None, "Transition Playing -> None is not allowed.")]
    [InlineData(PlaybackState.Playing, PlaybackState.Opening, "Transition Playing -> Opening is not allowed.")]
    [InlineData(PlaybackState.Playing, PlaybackState.Playing, "Transition Playing -> Playing is not allowed.")]
    [InlineData(PlaybackState.Paused, PlaybackState.None, "Transition Paused -> None is not allowed.")]
    [InlineData(PlaybackState.Paused, PlaybackState.Opening, "Transition Paused -> Opening is not allowed.")]
    [InlineData(PlaybackState.Paused, PlaybackState.Paused, "Transition Paused -> Paused is not allowed.")]
    [InlineData(PlaybackState.Paused, PlaybackState.Ended, "Transition Paused -> Ended is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.None, "Transition Stopped -> None is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.Playing, "Transition Stopped -> Playing is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.Paused, "Transition Stopped -> Paused is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.Stopped, "Transition Stopped -> Stopped is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.Ended, "Transition Stopped -> Ended is not allowed.")]
    [InlineData(PlaybackState.Stopped, PlaybackState.Error, "Transition Stopped -> Error is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.None, "Transition Ended -> None is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.Playing, "Transition Ended -> Playing is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.Paused, "Transition Ended -> Paused is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.Stopped, "Transition Ended -> Stopped is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.Ended, "Transition Ended -> Ended is not allowed.")]
    [InlineData(PlaybackState.Ended, PlaybackState.Error, "Transition Ended -> Error is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.None, "Transition Error -> None is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.Playing, "Transition Error -> Playing is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.Paused, "Transition Error -> Paused is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.Stopped, "Transition Error -> Stopped is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.Ended, "Transition Error -> Ended is not allowed.")]
    [InlineData(PlaybackState.Error, PlaybackState.Error, "Transition Error -> Error is not allowed.")]
    public void TryTransition_RejectsUndocumentedTransitions(
        PlaybackState current,
        PlaybackState next,
        string expectedReason)
    {
        var machine = new PlaybackStateMachine(current);

        var accepted = machine.TryTransition(next, out var reason);

        Assert.False(accepted);
        Assert.Equal(current, machine.Current);
        Assert.Equal(expectedReason, reason);
    }
}
