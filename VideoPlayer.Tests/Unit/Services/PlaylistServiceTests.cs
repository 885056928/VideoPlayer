using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaylistServiceTests
{
    [Fact]
    public async Task AddFilesAsync_DeduplicatesAndPreservesOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "Movie1.mp4");
            var second = Path.Combine(root, "Movie2.mkv");
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            var service = new PlaylistService(new FileService());

            var result = await service.AddFilesAsync([first, first.ToUpperInvariant(), second], false);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value!.Count);
            Assert.Equal(2, service.Items.Count);
            Assert.Equal(0, service.CurrentIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFilesAsync_ReturnsReadOnlyViewsAndRaisesBatchEventsOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var first = Path.Combine(root, "Movie1.mp4");
            var second = Path.Combine(root, "Movie2.mp4");
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            var service = new PlaylistService(new FileService());
            var playlistChangedCount = 0;
            var currentChangedCount = 0;
            service.PlaylistChanged += (_, _) => playlistChangedCount++;
            service.CurrentChanged += (_, _) => currentChangedCount++;

            var result = await service.AddFilesAsync([first, second], false);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, playlistChangedCount);
            Assert.Equal(1, currentChangedCount);
            var items = Assert.IsAssignableFrom<IList<PlaylistItem>>(service.Items);
            Assert.Throws<NotSupportedException>((Action)(() => items.Add(service.Items[0])));
            var added = Assert.IsAssignableFrom<IList<PlaylistItem>>(result.Value!);
            Assert.Throws<NotSupportedException>((Action)(() => added.Clear()));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFolderAsync_AddsSupportedVideosInNaturalOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var movie10 = Path.Combine(root, "Movie10.mp4");
            var movie2 = Path.Combine(root, "Movie2.mkv");
            var notes = Path.Combine(root, "notes.txt");
            File.WriteAllText(movie10, string.Empty);
            File.WriteAllText(movie2, string.Empty);
            File.WriteAllText(notes, string.Empty);
            var service = new PlaylistService(new FileService());

            var result = await service.AddFolderAsync(root, false);

            Assert.True(result.IsSuccess);
            Assert.Collection(
                service.Items,
                item => Assert.EndsWith("Movie2.mkv", item.FilePath),
                item => Assert.EndsWith("Movie10.mp4", item.FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetNext_AtLastItem_ReturnsInvalidStateWithoutWrapping()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        var result = service.GetNext();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        Assert.Equal(1, service.CurrentIndex);
    }

    [Fact]
    public void GetPrevious_AtFirstItem_ReturnsInvalidStateWithoutWrapping()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 0);

        var result = service.GetPrevious();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        Assert.Equal(0, service.CurrentIndex);
    }

    [Fact]
    public void GetNext_WithWrap_AtLastItem_ReturnsFirstItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        var result = service.GetNext(wrap: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("a.mp4", result.Value!.FilePath);
        Assert.Equal(0, service.CurrentIndex);
    }

    [Fact]
    public void GetPrevious_WithWrap_AtFirstItem_ReturnsLastItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 0);

        var result = service.GetPrevious(wrap: true);

        Assert.True(result.IsSuccess);
        Assert.Equal("b.mp4", result.Value!.FilePath);
        Assert.Equal(1, service.CurrentIndex);
    }

    [Fact]
    public void SetCurrent_UsesCaseInsensitivePathMatching()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 0);

        var result = service.SetCurrent("B.MP4");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CurrentIndex);
        Assert.Equal("b.mp4", service.Current!.FilePath);
    }

    [Fact]
    public void SetCurrent_WhenFileIsMissing_ReturnsInvalidState()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4"], 0);

        var result = service.SetCurrent("missing.mp4");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
        Assert.Equal(0, service.CurrentIndex);
    }

    [Fact]
    public async Task RemoveAsync_WhenRemovingItemBeforeCurrent_PreservesCurrentItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4", "c.mp4"], 2);

        var result = await service.RemoveAsync("A.MP4");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, service.Items.Count);
        Assert.Equal(1, service.CurrentIndex);
        Assert.Equal("c.mp4", service.Current!.FilePath);
    }

    [Fact]
    public async Task ClearAsync_ResetsPlaylistAndCurrentSelection()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        var result = await service.ClearAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(service.Items);
        Assert.Equal(-1, service.CurrentIndex);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task AddFilesAsync_PreservesInputOrderAcrossBatches()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var third = Path.Combine(root, "Third.mp4");
            var first = Path.Combine(root, "First.mp4");
            var second = Path.Combine(root, "Second.mp4");
            File.WriteAllText(third, string.Empty);
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            var service = new PlaylistService(new FileService());

            await service.AddFilesAsync([third, first], false);
            var secondResult = await service.AddFilesAsync([second, third], false);

            Assert.True(secondResult.IsSuccess);
            Assert.Equal(
                new[] { third, first, second },
                service.Items.Select(item => item.FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RemoveAsync_WhenRemovingCurrentItem_SelectsNextItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4", "c.mp4"], 1);

        var result = await service.RemoveAsync("B.MP4");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CurrentIndex);
        Assert.Equal("c.mp4", service.Current!.FilePath);
    }

    [Fact]
    public async Task RemoveAsync_WhenRemovingItemAfterCurrent_PreservesCurrentItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4", "c.mp4"], 1);

        var result = await service.RemoveAsync("C.MP4");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CurrentIndex);
        Assert.Equal("b.mp4", service.Current!.FilePath);
    }

    [Fact]
    public async Task RemoveAsync_WhenRemovingFinalCurrentItem_SelectsPreviousItem()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4", "c.mp4"], 2);

        var result = await service.RemoveAsync("C.MP4");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CurrentIndex);
        Assert.Equal("b.mp4", service.Current!.FilePath);
    }

    [Fact]
    public async Task ClearAsync_CanBeCalledRepeatedly()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        var first = await service.ClearAsync();
        var second = await service.ClearAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Empty(service.Items);
        Assert.Equal(-1, service.CurrentIndex);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task AddFilesAsync_WithPreCanceledToken_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PlaylistService(new FileService());

        var result = await service.AddFilesAsync([], false, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task AddFolderAsync_WithPreCanceledToken_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PlaylistService(new FileService());

        var result = await service.AddFolderAsync("unused", false, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task RemoveAsync_WithPreCanceledToken_ReturnsCancelledWithoutMutation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4"], 0);

        var result = await service.RemoveAsync("a.mp4", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.Single(service.Items);
    }

    [Fact]
    public async Task ClearAsync_WithPreCanceledToken_ReturnsCancelledWithoutMutation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4"], 0);

        var result = await service.ClearAsync(cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.Single(service.Items);
    }

    [Theory]
    [InlineData(ErrorCode.NoPermission)]
    [InlineData(ErrorCode.Unknown)]
    [InlineData(ErrorCode.FileNotFound)]
    public async Task AddFolderAsync_WhenEnumerationFailsWithoutFiles_ReturnsFolderError(ErrorCode errorCode)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new PlaylistService(new FailingEnumerationFileService(errorCode));

            var result = await service.AddFolderAsync(root, false);

            Assert.False(result.IsSuccess);
            Assert.Equal(errorCode, result.ErrorCode);
            Assert.Equal("folder-error", result.TechnicalMessage);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFolderAsync_ProbesAndEnumeratesOnBackgroundThread()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fileService = new RecordingEnumerationFileService();
            var service = new PlaylistService(fileService);
            var callerThreadId = 0;
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                callerThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    var result = service.AddFolderAsync(root, false).GetAwaiter().GetResult();
                    Assert.True(result.IsSuccess);
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            });

            thread.Start();
            thread.Join();

            Assert.Null(exception);
            Assert.NotEqual(callerThreadId, fileService.EnumerationThreadId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFilesAsync_WithSynchronizationContext_RaisesEventsOnContextThread()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            int? playlistThreadId = null;
            int? currentThreadId = null;
            service.PlaylistChanged += (_, _) => playlistThreadId = Environment.CurrentManagedThreadId;
            service.CurrentChanged += (_, _) => currentThreadId = Environment.CurrentManagedThreadId;
            using var context = new SingleThreadSynchronizationContext();

            await context.RunAsync(async () =>
            {
                var result = await service.AddFilesAsync([file], false);
                Assert.True(result.IsSuccess);
            });

            Assert.Equal(context.ThreadId, playlistThreadId);
            Assert.Equal(context.ThreadId, currentThreadId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFilesAsync_WithoutSynchronizationContext_RaisesEventsOnWorkerThread()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            int? playlistThreadId = null;
            int? currentThreadId = null;
            service.PlaylistChanged += (_, _) => playlistThreadId = Environment.CurrentManagedThreadId;
            service.CurrentChanged += (_, _) => currentThreadId = Environment.CurrentManagedThreadId;
            var callerThreadId = 0;
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                callerThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    var result = service.AddFilesAsync([file], false).GetAwaiter().GetResult();
                    Assert.True(result.IsSuccess);
                }
                catch (Exception caught)
                {
                    exception = caught;
                }
            });

            thread.Start();
            thread.Join();

            Assert.Null(exception);
            Assert.NotEqual(callerThreadId, playlistThreadId);
            Assert.NotEqual(callerThreadId, currentThreadId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddFilesAsync_WhenCancellationRequestedDuringEnumeration_ReturnsCancelledWithoutMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var cancellation = new CancellationTokenSource();
            var service = new PlaylistService(new CancelingEnumerationFileService(cancellation));

            var result = await service.AddFolderAsync(root, false, cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
            Assert.Empty(service.Items);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public async Task SetCurrent_RaisesOnlyCurrentChangedOnCapturedContext()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 0);

        await AssertSelectionOnlyChangeAsync(service, () => service.SetCurrent("b.mp4"));
    }

    [Fact]
    public async Task GetNext_RaisesOnlyCurrentChangedOnCapturedContext()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 0);

        await AssertSelectionOnlyChangeAsync(service, () => service.GetNext());
    }

    [Fact]
    public async Task GetPrevious_RaisesOnlyCurrentChangedOnCapturedContext()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        await AssertSelectionOnlyChangeAsync(service, () => service.GetPrevious());
    }

    private static async Task AssertSelectionOnlyChangeAsync(
        PlaylistService service,
        Action selectionChange)
    {
        var playlistChangedCount = 0;
        var currentChangedCount = 0;
        int? playlistThreadId = null;
        int? currentThreadId = null;
        var currentChanged = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.PlaylistChanged += (_, _) =>
        {
            playlistChangedCount++;
            playlistThreadId = Environment.CurrentManagedThreadId;
        };
        service.CurrentChanged += (_, _) =>
        {
            currentChangedCount++;
            currentThreadId = Environment.CurrentManagedThreadId;
            currentChanged.TrySetResult(null);
        };

        using var context = new SingleThreadSynchronizationContext();
        await context.RunAsync(async () =>
        {
            selectionChange();
            await currentChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.Equal(0, playlistChangedCount);
        Assert.Equal(1, currentChangedCount);
        Assert.Null(playlistThreadId);
        Assert.Equal(context.ThreadId, currentThreadId);
    }
    private sealed class FailingEnumerationFileService : IFileService
    {
        private readonly ErrorCode _errorCode;

        public FailingEnumerationFileService(ErrorCode errorCode)
        {
            _errorCode = errorCode;
        }

        public bool IsSupportedVideo(string filePath) => true;

        public string NormalizePath(string filePath) => PathHelper.NormalizePath(filePath);

        public Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
            string folderPath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ServiceResult<IReadOnlyList<string>>.Failure(
                _errorCode,
                "folder-error",
                "folder-error"));
        }
    }

    private sealed class CancelingEnumerationFileService : IFileService
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelingEnumerationFileService(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public bool IsSupportedVideo(string filePath) => true;

        public string NormalizePath(string filePath) => PathHelper.NormalizePath(filePath);

        public Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
            string folderPath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            IReadOnlyList<string> paths = [Path.Combine(folderPath, "movie.mp4")];
            return Task.FromResult(ServiceResult<IReadOnlyList<string>>.Success(paths));
        }
    }
    private sealed class RecordingEnumerationFileService : IFileService
    {
        public int EnumerationThreadId { get; private set; }

        public bool IsSupportedVideo(string filePath) => true;

        public string NormalizePath(string filePath) => PathHelper.NormalizePath(filePath);

        public Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
            string folderPath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            EnumerationThreadId = Environment.CurrentManagedThreadId;
            IReadOnlyList<string> paths = [Path.Combine(folderPath, "movie.mp4")];
            return Task.FromResult(ServiceResult<IReadOnlyList<string>>.Success(paths));
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly TaskCompletionSource<object?> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;
        private bool _disposed;

        public SingleThreadSynchronizationContext()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
            _started.Task.GetAwaiter().GetResult();
        }

        public int ThreadId { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add((d, state));
            }
        }

        public async Task RunAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Post(async _ =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    completion.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }, null);

            await completion.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _stopped.Task.Wait(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private void Run()
        {
            SynchronizationContext.SetSynchronizationContext(this);
            ThreadId = Environment.CurrentManagedThreadId;
            _started.TrySetResult(null);

            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }

            _stopped.TrySetResult(null);
        }
    }
}
