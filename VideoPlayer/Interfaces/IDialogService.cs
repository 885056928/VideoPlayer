using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IDialogService
{
    Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default);
}