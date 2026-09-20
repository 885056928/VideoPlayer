using System.Windows;

using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default)
    {
        var result = MessageBox.Show(
            "是否继续播放 " + fileName + "？\n\n上次播放位置：" +
            lastPosition.ToString(@"hh\:mm\:ss"),
            "继续播放",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => ResumeChoice.Continue,
            MessageBoxResult.No => ResumeChoice.Restart,
            _ => ResumeChoice.Cancel
        });
    }
}
