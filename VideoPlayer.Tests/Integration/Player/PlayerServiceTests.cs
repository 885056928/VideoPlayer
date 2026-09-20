using LibVLCSharp.Shared;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Player;

public sealed class PlayerServiceTests
{
    [Fact]
    public async Task Constructor_InitializesLibVlcAndDisposesIdempotently()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var service = new PlayerService(
            new ImmediateUiDispatcher(),
            logger,
            ["--vout=dummy"]);

        Assert.True(service.IsInitialized);
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_MissingFileReturnsFileNotFound()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        await using var service = new PlayerService(
            new ImmediateUiDispatcher(),
            logger,
            ["--vout=dummy"]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

        var result = await service.OpenAsync(
            new OpenMediaRequest(path, TimeSpan.Zero, true, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task DisposedService_AccessorsReturnSafeDefaults()
    {
        var service = CreateService();

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Null(service.MediaPlayer);
        Assert.False(service.HasMedia);

        var snapshot = service.Snapshot;

        Assert.Equal(0, snapshot.MediaSessionId);
        Assert.Equal(PlaybackState.None, snapshot.State);
        Assert.Null(snapshot.MediaPath);
        Assert.Equal(TimeSpan.Zero, snapshot.Position);
        Assert.Equal(TimeSpan.Zero, snapshot.Duration);
        Assert.Equal(0, snapshot.Volume);
        Assert.False(snapshot.IsMuted);
        Assert.Equal(1.0, snapshot.Rate);
        Assert.False(snapshot.IsSeekable);
        Assert.False(snapshot.HasVideo);
        Assert.False(snapshot.HasAudio);
    }

    [Fact]
    public async Task StaleMediaCallback_IsIgnoredAfterNewSessionOpened()
    {
        var firstPath = CreateTempMediaFile();
        var secondPath = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var first = await service.OpenAsync(CreateRequest(firstPath));
            Assert.True(first.IsSuccess);
            await service.StopAsync();

            var second = await service.OpenAsync(CreateRequest(secondPath));
            Assert.True(second.IsSuccess);

            var stateChanges = new List<PlaybackStateChangedEventArgs>();
            service.StateChanged += (_, args) => stateChanges.Add(args);

            service.HandleMediaEvent(first.Value!.MediaSessionId, PlaybackState.Playing);
            service.HandleMediaEvent(second.Value!.MediaSessionId, PlaybackState.Playing);

            var stateChange = Assert.Single(stateChanges);
            Assert.Equal(second.Value.MediaSessionId, stateChange.MediaSessionId);
            Assert.Equal(PlaybackState.Playing, stateChange.NewState);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task DelayedStaleEvents_AreSuppressedAfterNewSessionOpened()
    {
        var firstPath = CreateTempMediaFile();
        var secondPath = CreateTempMediaFile();
        var dispatcher = new QueueUiDispatcher();
        await using var service = new PlayerService(
            dispatcher,
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);
        var stateChanges = new List<PlaybackStateChangedEventArgs>();
        var mediaOpened = new List<MediaOpenedEventArgs>();
        service.StateChanged += (_, args) => stateChanges.Add(args);
        service.MediaOpened += (_, args) => mediaOpened.Add(args);

        try
        {
            var first = await service.OpenAsync(CreateRequest(firstPath));
            Assert.True(first.IsSuccess);
            await service.StopAsync();
            var second = await service.OpenAsync(CreateRequest(secondPath));
            Assert.True(second.IsSuccess);

            dispatcher.Flush();

            Assert.NotEmpty(stateChanges);
            Assert.NotEmpty(mediaOpened);
            Assert.All(
                stateChanges,
                args => Assert.Equal(second.Value!.MediaSessionId, args.MediaSessionId));
            Assert.All(
                mediaOpened,
                args => Assert.Equal(second.Value!.MediaSessionId, args.MediaSessionId));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task NativeCallbackException_IsContainedAndReportsFailure()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);

            PlaybackFailedEventArgs? failure = null;
            service.StateChanged += (_, _) =>
                throw new InvalidOperationException("callback failure");
            service.PlaybackFailed += (_, args) => failure = args;
            service.PlaybackFailed += (_, _) =>
                throw new ApplicationException("failure subscriber");

            var exception = Record.Exception(() => service.HandleMediaEvent(
                opened.Value!.MediaSessionId,
                PlaybackState.Playing));

            Assert.Null(exception);
            Assert.NotNull(failure);
            Assert.Equal(ErrorCode.Unknown, failure.ErrorCode);
            Assert.IsType<InvalidOperationException>(failure.Exception);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public async Task ServiceProviderDisposeAsync_DisposesPlayerServiceIdempotently()
    {
        var service = CreateService();
        var services = new ServiceCollection();
        services.AddSingleton<IPlayerService>(_ => service);
        var provider = services.BuildServiceProvider();
        Assert.Same(service, provider.GetRequiredService<IPlayerService>());

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        Assert.False(service.IsInitialized);
        Assert.Null(service.MediaPlayer);
    }
    [Fact]
    public async Task SetVolumeAsync_ClampsToBoundaries()
    {
        await using var service = CreateService();
        var cases = new[]
        {
            (Input: -10, Expected: 0),
            (Input: 0, Expected: 0),
            (Input: 55, Expected: 55),
            (Input: 100, Expected: 100),
            (Input: 500, Expected: 100)
        };

        foreach (var item in cases)
        {
            var result = await service.SetVolumeAsync(item.Input);

            Assert.True(result.IsSuccess);
            Assert.Equal(item.Expected, service.Snapshot.Volume);
        }
    }

    [Fact]
    public async Task SetRateAsync_ClampsToBoundaries()
    {
        await using var service = CreateService();
        var cases = new[]
        {
            (Input: 0.1, Expected: 0.5),
            (Input: 0.5, Expected: 0.5),
            (Input: 1.25, Expected: 1.25),
            (Input: 2.0, Expected: 2.0),
            (Input: 9.0, Expected: 2.0),
            (Input: double.NaN, Expected: 1.0),
            (Input: double.PositiveInfinity, Expected: 1.0),
            (Input: double.NegativeInfinity, Expected: 1.0)
        };

        foreach (var item in cases)
        {
            var result = await service.SetRateAsync(item.Input);

            Assert.True(result.IsSuccess);
            Assert.Equal(item.Expected, service.Snapshot.Rate);
        }
    }

    [Fact]
    public async Task SeekAsync_WhenMediaIsUnseekable_ReturnsSeekNotAvailable()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);

            var result = await service.SeekAsync(TimeSpan.FromSeconds(1));

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.SeekNotAvailable, result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public async Task HandleLengthChanged_PublishesParsedAndDeduplicatedTrackEvents()
    {
        var path = CreateTempMediaFile();
        var dispatcher = new QueueUiDispatcher();
        await using var service = new PlayerService(
            dispatcher,
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);
        var parsedEvents = new List<MediaParsedEventArgs>();
        var trackEvents = new List<TracksChangedEventArgs>();
        service.MediaParsed += (_, args) => parsedEvents.Add(args);
        service.TracksChanged += (_, args) => trackEvents.Add(args);

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);

            service.HandleLengthChanged(opened.Value!.MediaSessionId, 1234);
            service.HandleLengthChanged(opened.Value.MediaSessionId, 1234);

            Assert.Empty(parsedEvents);
            dispatcher.Flush();

            Assert.Equal(2, parsedEvents.Count);
            Assert.All(
                parsedEvents,
                args => Assert.Equal(
                    TimeSpan.FromMilliseconds(1234),
                    args.Duration));
            var trackEvent = Assert.Single(trackEvents);
            Assert.Equal(opened.Value.MediaSessionId, trackEvent.MediaSessionId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleMediaParsedChanged_Done_PublishesParsedEvent()
    {
        var path = CreateTempMediaFile();
        var dispatcher = new QueueUiDispatcher();
        await using var service = new PlayerService(
            dispatcher,
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);
        var parsedEvents = new List<MediaParsedEventArgs>();
        service.MediaParsed += (_, args) => parsedEvents.Add(args);

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);

            service.HandleMediaParsedChanged(
                opened.Value!.MediaSessionId,
                MediaParsedStatus.Skipped);
            dispatcher.Flush();
            Assert.Empty(parsedEvents);

            service.HandleMediaParsedChanged(
                opened.Value.MediaSessionId,
                MediaParsedStatus.Done);
            dispatcher.Flush();

            var parsed = Assert.Single(parsedEvents);
            Assert.Equal(opened.Value.MediaSessionId, parsed.MediaSessionId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DelayedStaleTrackEvents_AreSuppressedAfterNewSessionOpened()
    {
        var firstPath = CreateTempMediaFile();
        var secondPath = CreateTempMediaFile();
        var dispatcher = new QueueUiDispatcher();
        await using var service = new PlayerService(
            dispatcher,
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);
        var parsedEvents = new List<MediaParsedEventArgs>();
        service.MediaParsed += (_, args) => parsedEvents.Add(args);

        try
        {
            var first = await service.OpenAsync(CreateRequest(firstPath));
            Assert.True(first.IsSuccess);
            var second = await service.OpenAsync(CreateRequest(secondPath));
            Assert.True(second.IsSuccess);

            service.HandleLengthChanged(first.Value!.MediaSessionId, 111);
            service.HandleLengthChanged(second.Value!.MediaSessionId, 222);
            dispatcher.Flush();

            var parsed = Assert.Single(parsedEvents);
            Assert.Equal(second.Value.MediaSessionId, parsed.MediaSessionId);
            Assert.Equal(TimeSpan.FromMilliseconds(222), parsed.Duration);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }
    [Fact]
    public async Task SelectAudioTrackAsync_WhenTrackDoesNotExist_DoesNotChangePlaybackState()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            var state = service.Snapshot.State;

            var result = await service.SelectAudioTrackAsync(999);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.TrackNotFound, result.ErrorCode);
            Assert.Equal(state, service.Snapshot.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddExternalSubtitleAsync_WhenFileIsMissing_DoesNotChangePlaybackState()
    {
        var path = CreateTempMediaFile();
        var subtitlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            var state = service.Snapshot.State;

            var result = await service.AddExternalSubtitleAsync(
                subtitlePath,
                select: true);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.SubtitleNotFound, result.ErrorCode);
            Assert.Equal(state, service.Snapshot.State);
        }
        finally
        {
            File.Delete(path);
        }
    }
    private static PlayerService CreateService()
    {
        return new PlayerService(
            new ImmediateUiDispatcher(),
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);
    }
    private static OpenMediaRequest CreateRequest(string path)
    {
        return new OpenMediaRequest(path, TimeSpan.Zero, false, null, null);
    }

    private static string CreateTempMediaFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        File.WriteAllBytes(path, []);
        return path;
    }
    private sealed class QueueUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public bool CheckAccess() => true;

        public void Post(Action action) => _pending.Enqueue(action);

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

        public void Flush()
        {
            while (_pending.TryDequeue(out var action))
            {
                action();
            }
        }
    }
    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
