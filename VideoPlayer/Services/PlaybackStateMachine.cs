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

    public PlaybackStateMachine(PlaybackState initial = PlaybackState.None)
    {
        Current = initial;
    }

    public PlaybackState Current { get; private set; }

    public bool TryTransition(PlaybackState next, out string? rejectionReason)
    {
        if (Allowed.TryGetValue(Current, out var targets) && targets.Contains(next))
        {
            Current = next;
            rejectionReason = null;
            return true;
        }

        rejectionReason = $"Transition {Current} -> {next} is not allowed.";
        return false;
    }
}
