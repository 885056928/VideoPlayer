using System.IO;
using LibVLCSharp.Shared;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Player;

public sealed class PlayerServiceMediaReplacementTests
{
    private static readonly byte[] JpegFrame = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAgACADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KK5Cjh3h3+2fafvOTkt0ve9/Ndj9Qz7Pv7J9n+75+e/W1rW8n3OvorkK6+jiLh3+xvZ/vOfnv0ta1vN9wyHPv7W9p+75OS3W9738l2CuQrr6KOHeIv7G9p+75+e3W1rX8n3DPsh/tb2f7zk5L9L3vbzXY5Cuvooo4i4i/tn2f7vk5L9b3vbyXYMhyH+yfafvOfnt0ta1/N9z//Z");

    [Fact]
    public async Task OpenAsync_WhenMediaIsPlaying_StopsOldSessionAndStartsNewSession()
    {
        var firstPath = CreateMediaFile();
        var secondPath = CreateMediaFile();
        await using var service = CreateService();
        var states = new List<PlaybackStateChangedEventArgs>();
        service.StateChanged += (_, args) => states.Add(args);

        try
        {
            var first = await service.OpenAsync(CreateRequest(firstPath));
            Assert.True(first.IsSuccess);
            await WaitForStateAsync(service, first.Value!.MediaSessionId, PlaybackState.Playing);

            var second = await service.OpenAsync(CreateRequest(secondPath));

            Assert.True(second.IsSuccess);
            Assert.NotEqual(first.Value.MediaSessionId, second.Value!.MediaSessionId);
            Assert.Equal(Path.GetFullPath(secondPath), service.Snapshot.MediaPath);
            Assert.Contains(
                states,
                args => args.MediaSessionId == first.Value.MediaSessionId
                    && args.NewState == PlaybackState.Stopped);
            await WaitForStateAsync(service, second.Value.MediaSessionId, PlaybackState.Playing);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task OpenAsync_WhenMediaIsPaused_StopsOldSessionAndStartsNewSession()
    {
        var firstPath = CreateMediaFile();
        var secondPath = CreateMediaFile();
        await using var service = CreateService();
        var states = new List<PlaybackStateChangedEventArgs>();
        service.StateChanged += (_, args) => states.Add(args);

        try
        {
            var first = await service.OpenAsync(CreateRequest(firstPath));
            Assert.True(first.IsSuccess);
            await WaitForStateAsync(service, first.Value!.MediaSessionId, PlaybackState.Playing);
            var paused = await service.PauseAsync();
            Assert.True(paused.IsSuccess);
            await WaitForStateAsync(service, first.Value.MediaSessionId, PlaybackState.Paused);

            var second = await service.OpenAsync(CreateRequest(secondPath));

            Assert.True(second.IsSuccess);
            Assert.Equal(Path.GetFullPath(secondPath), service.Snapshot.MediaPath);
            Assert.Contains(
                states,
                args => args.MediaSessionId == first.Value.MediaSessionId
                    && args.NewState == PlaybackState.Stopped);
            await WaitForStateAsync(service, second.Value!.MediaSessionId, PlaybackState.Playing);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task AddExternalSubtitleAsync_WhenSuccessful_PublishesTracksChanged()
    {
        var mediaPath = CreateMediaFile();
        var subtitlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            "1\n00:00:00,000 --> 00:00:02,000\nVideoPlayer\n");
        await using var service = CreateService();
        var trackEvents = new List<TracksChangedEventArgs>();
        service.TracksChanged += (_, args) => trackEvents.Add(args);

        try
        {
            var opened = await service.OpenAsync(CreateRequest(mediaPath));
            Assert.True(opened.IsSuccess);
            await WaitForStateAsync(service, opened.Value!.MediaSessionId, PlaybackState.Playing);
            trackEvents.Clear();

            var result = await service.AddExternalSubtitleAsync(subtitlePath, select: true);

            Assert.True(result.IsSuccess, result.TechnicalMessage);
            Assert.NotEmpty(trackEvents);
            Assert.All(
                trackEvents,
                args => Assert.Equal(opened.Value.MediaSessionId, args.MediaSessionId));
        }
        finally
        {
            File.Delete(mediaPath);
            File.Delete(subtitlePath);
        }
    }

    private static PlayerService CreateService()
        => new(
            new ImmediateUiDispatcher(),
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy", "--aout=dummy", "--no-audio"]);

    private static OpenMediaRequest CreateRequest(string path)
        => new(path, TimeSpan.Zero, AutoPlay: true, null, null);

    private static async Task WaitForStateAsync(
        PlayerService service,
        long sessionId,
        PlaybackState expected)
    {
        if (service.Snapshot.MediaSessionId == sessionId
            && service.Snapshot.State == expected)
        {
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<PlaybackStateChangedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.MediaSessionId == sessionId && args.NewState == expected)
            {
                service.StateChanged -= handler;
                completion.TrySetResult();
            }
        };
        service.StateChanged += handler;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            service.StateChanged -= handler;
        }
    }

    private static string CreateMediaFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mjpeg");
        using var stream = File.Create(path);
        for (var index = 0; index < 30; index++)
        {
            stream.Write(JpegFrame);
        }

        return path;
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
