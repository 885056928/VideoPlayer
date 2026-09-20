using VideoPlayer.Models;

namespace VideoPlayer.Services;

public static class ResumePolicy
{
    public static bool ShouldAsk(ResumeInfo info)
    {
        if (info.IsCompleted || info.Duration < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        if (info.LastPosition < TimeSpan.FromSeconds(15))
        {
            return false;
        }

        if (info.LastPosition >= info.Duration * 0.95)
        {
            return false;
        }

        return info.Duration - info.LastPosition >= TimeSpan.FromSeconds(10);
    }
}
