using System.IO;

using VideoPlayer.Services;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlaylistViewModelTests
{
    [Fact]
    public async Task AddPathsAsync_UpdatesObservableItems()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service);

            await viewModel.AddPathsAsync([file]);

            Assert.Single(viewModel.Items);
            Assert.Equal(file, viewModel.Items[0].FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddPathsAsync_WithFolder_DropsSupportedVideos()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service);

            await viewModel.AddPathsAsync([root]);

            Assert.Single(viewModel.Items);
            Assert.Equal(file, viewModel.Items[0].FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RemoveAsync_UpdatesObservableItems()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service);
            await viewModel.AddPathsAsync([file]);

            await viewModel.RemoveAsync(viewModel.Items[0]);

            Assert.Empty(viewModel.Items);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ClearCommand_ClearsObservableItems()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service);
            await viewModel.AddPathsAsync([file]);

            await viewModel.ClearCommand.ExecuteAsync(null);

            Assert.Empty(viewModel.Items);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
