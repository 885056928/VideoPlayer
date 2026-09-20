using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IDialogService
{
    Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default);

    Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);

    Task ShowInformationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);
}