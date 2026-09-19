using System.IO;

using VideoPlayer.Enums;
using VideoPlayer.Models;
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
}
