using System.Collections.Specialized;
using System.IO;

using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
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
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());

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
    public async Task AddFolderAsync_ScansAndUpdatesObservableItems()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());

            await viewModel.AddFolderAsync(root);

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
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());

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
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());
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
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());
            await viewModel.AddPathsAsync([file]);

            await viewModel.ClearCommand.ExecuteAsync(null);

            Assert.Empty(viewModel.Items);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PlaylistChanged_FromOffThread_PostsUpdateToDispatcher()
    {
        var item = new PlaylistItem { FilePath = @"C:\Videos\movie.mp4" };
        var service = new FakePlaylistService();
        service.SetItems([item]);
        var dispatcher = new QueuedUiDispatcher();
        var viewModel = new PlaylistViewModel(service, dispatcher);

        await Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            service.RaisePlaylistChanged();
        });

        Assert.Empty(viewModel.Items);
        Assert.Equal(1, dispatcher.PostCount);
        Assert.True(dispatcher.RunNext());
        Assert.Single(viewModel.Items);
        Assert.Same(item, viewModel.Items[0]);
    }

    [Fact]
    public async Task AddPathsAsync_AppendsWithoutResetAndPreservesExistingItems()
    {
        var service = new FakePlaylistService();
        var dispatcher = new ImmediateUiDispatcher();
        var viewModel = new PlaylistViewModel(service, dispatcher);
        await viewModel.AddPathsAsync([@"C:\Videos\first.mp4"]);
        var first = viewModel.Items[0];
        var resetCount = 0;
        var addCount = 0;
        viewModel.Items.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resetCount++;
            }

            if (args.Action == NotifyCollectionChangedAction.Add)
            {
                addCount++;
            }
        };

        await viewModel.AddPathsAsync([@"C:\Videos\second.mp4"]);

        Assert.Equal(2, viewModel.Items.Count);
        Assert.Same(first, viewModel.Items[0]);
        Assert.Equal(@"C:\Videos\first.mp4", viewModel.Items[0].FilePath);
        Assert.Equal(@"C:\Videos\second.mp4", viewModel.Items[1].FilePath);
        Assert.Equal(1, addCount);
        Assert.Equal(0, resetCount);
        Assert.Equal(service.PlaylistChangedCount, dispatcher.PostCount);
    }

    [Fact]
    public void TenThousandItemProjection_UsesBulkResetInsteadOfIndividualAdds()
    {
        var service = new FakePlaylistService();
        var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());
        service.SetItems(Enumerable.Range(0, 10_000)
            .Select(index => new PlaylistItem { FilePath = $"video-{index:D5}.mp4" }));
        var resets = 0;
        var adds = 0;
        viewModel.Items.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
            else if (args.Action == NotifyCollectionChangedAction.Add)
            {
                adds++;
            }
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        service.RaisePlaylistChanged();
        stopwatch.Stop();

        Assert.Equal(10_000, viewModel.Items.Count);
        Assert.Equal(1, resets);
        Assert.Equal(0, adds);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"10k projection took {stopwatch.Elapsed.TotalSeconds:F3} seconds.");
    }

    [Fact]
    public async Task AddPathsAsync_MarksFirstItemCurrent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service, new ImmediateUiDispatcher());

            await viewModel.AddPathsAsync([file]);

            Assert.True(viewModel.Items[0].IsCurrent);
            Assert.Equal(0, service.CurrentIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public bool CheckAccess() => true;

        public void Post(Action action)
        {
            PostCount++;
            action();
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            return Task.FromResult(action());
        }
    }

    private sealed class QueuedUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = [];

        public int PostCount { get; private set; }

        public bool CheckAccess() => false;

        public void Post(Action action)
        {
            PostCount++;
            _actions.Enqueue(action);
        }

        public Task InvokeAsync(Action action)
        {
            Post(action);
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            return Task.FromResult(action());
        }

        public bool RunNext()
        {
            if (!_actions.TryDequeue(out var action))
            {
                return false;
            }

            action();
            return true;
        }
    }

    private sealed class FakePlaylistService : IPlaylistService
    {
        private readonly List<PlaylistItem> _items = [];

        public IReadOnlyList<PlaylistItem> Items => _items;

        public int CurrentIndex => _items.Count == 0 ? -1 : 0;

        public PlaylistItem? Current => _items.Count == 0 ? null : _items[0];

        public int PlaylistChangedCount { get; private set; }

        public event EventHandler? PlaylistChanged;

        public event EventHandler? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
            IEnumerable<string> paths,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            foreach (var path in paths)
            {
                _items.Add(new PlaylistItem { FilePath = path });
            }

            RaisePlaylistChanged();
            return Task.FromResult(
                ServiceResult<IReadOnlyList<PlaylistItem>>.Success(_items.ToArray()));
        }

        public Task<ServiceResult> AddFolderAsync(
            string folderPath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> RemoveAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            var item = _items.FirstOrDefault(
                candidate => string.Equals(
                    candidate.FilePath,
                    filePath,
                    StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                _items.Remove(item);
                RaisePlaylistChanged();
            }

            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
        {
            if (_items.Count > 0)
            {
                _items.Clear();
                RaisePlaylistChanged();
            }

            return Task.FromResult(ServiceResult.Success());
        }

        public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
        {
            return _items.Count == 0
                ? ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "Empty playlist.",
                    "Empty playlist.")
                : ServiceResult<PlaylistItem>.Success(_items[0]);
        }

        public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
        {
            return GetNext(wrap);
        }

        public ServiceResult SetCurrent(string filePath)
        {
            return ServiceResult.Success();
        }

        public void NotifyCurrentItemChanged()
        {
        }

        public void SetItems(IEnumerable<PlaylistItem> items)
        {
            _items.Clear();
            _items.AddRange(items);
        }

        public void RaisePlaylistChanged()
        {
            PlaylistChangedCount++;
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
