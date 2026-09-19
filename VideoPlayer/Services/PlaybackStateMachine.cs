using VideoPlayer.Enums;

namespace VideoPlayer.Services;

public sealed class PlaybackStateMachine
{
    private static readonly IReadOnlyDictionary<PlaybackState, PlaybackState[]> Allowed =
        new Dictionary<PlaybackState, PlaybackState[]>
        {
            [PlaybackState.None] = [PlaybackState.Opening],
            [PlaybackState.Opening] = [PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Error],
            [PlaybackState.Playing] = [PlaybackState.Paused, PlaybackState.Stopped, PlaybackState.Ended, PlaybackState.Error],
            [PlaybackState.Paused] = [PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Error],
            [PlaybackState.Stopped] = [PlaybackState.Opening],
            [PlaybackState.Ended] = [PlaybackState.Opening],
            [PlaybackState.Error] = [PlaybackState.Opening]
        };

    private readonly object _sync = new();
    private PlaybackState _current;

    public PlaybackStateMachine(PlaybackState initial = PlaybackState.None)
    {
        _current = initial;
    }

    public PlaybackState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool TryTransition(PlaybackState next, out string? rejectionReason)
    {
        return TryTransition(next, out _, out rejectionReason);
    }

    public bool TryTransition(
        PlaybackState next,
        out PlaybackState oldState,
        out string? rejectionReason)
    {
        lock (_sync)
        {
            oldState = _current;
            if (Allowed.TryGetValue(_current, out var targets) && targets.Contains(next))
            {
                _current = next;
                rejectionReason = null;
                return true;
            }

            rejectionReason = $"Transition {_current} -> {next} is not allowed.";
            return false;
        }
    }
}
