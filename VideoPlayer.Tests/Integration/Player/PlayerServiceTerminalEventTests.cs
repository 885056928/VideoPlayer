using System.IO;
using System.Reflection;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Player;

public sealed class PlayerServiceTerminalEventTests
{
    [Fact]
    public async Task HandleEndReached_RaisesOncePerSession()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();
        var events = new List<EndReachedEventArgs>();
        service.EndReached += (_, args) => events.Add(args);

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            var sessionId = opened.Value!.MediaSessionId;
            service.HandleMediaEvent(sessionId, PlaybackState.Playing);

            service.HandleEndReached(sessionId);
            service.HandleEndReached(sessionId);

            Assert.Single(events);
            Assert.Equal(sessionId, events[0].MediaSessionId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleMediaError_RaisesFailureOncePerSession()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();
        var failures = new List<PlaybackFailedEventArgs>();
        service.PlaybackFailed += (_, args) => failures.Add(args);

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            var sessionId = opened.Value!.MediaSessionId;
            service.HandleMediaEvent(sessionId, PlaybackState.Playing);

            service.HandleMediaError(sessionId);
            service.HandleMediaError(sessionId);

            Assert.Single(failures);
            Assert.Equal(sessionId, failures[0].MediaSessionId);
            Assert.Equal(PlaybackState.Error, service.Snapshot.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DisposeAsync_ClearsEndReachedHandlers()
    {
        var service = CreateService();
        service.EndReached += (_, _) => { };

        await service.DisposeAsync();

        var field = typeof(PlayerService).GetField(
            "EndReached",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field.GetValue(service));
    }

    private static PlayerService CreateService()
        => new(
            new ImmediateUiDispatcher(),
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy", "--aout=dummy", "--no-audio"]);

    private static OpenMediaRequest CreateRequest(string path)
        => new(path, TimeSpan.Zero, AutoPlay: false, null, null);

    private static string CreateTempMediaFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        File.WriteAllBytes(path, []);
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
