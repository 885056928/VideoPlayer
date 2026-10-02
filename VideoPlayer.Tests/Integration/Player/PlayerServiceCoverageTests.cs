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

public sealed class PlayerServiceCoverageTests
{
    [Fact]
    public async Task DisposedService_CommandMethodsReturnPlayerNotInitialized()
    {
        var service = CreateService();
        await service.DisposeAsync();
        var request = new OpenMediaRequest("movie.mp4", TimeSpan.Zero, false, null, null);

        var open = await service.OpenAsync(request);
        var play = await service.PlayAsync();
        var pause = await service.PauseAsync();
        var stop = await service.StopAsync();
        var seek = await service.SeekAsync(TimeSpan.FromSeconds(1));
        var volume = await service.SetVolumeAsync(50);
        var mute = await service.SetMuteAsync(true);
        var rate = await service.SetRateAsync(1.5);
        var tracks = await service.GetTracksAsync(PlayerTrackType.Audio);
        var audioTrack = await service.SelectAudioTrackAsync(1);
        var subtitleTrack = await service.SelectSubtitleTrackAsync(1);
        var subtitle = await service.AddExternalSubtitleAsync("subtitle.srt", select: true);
        var snapshot = await service.TakeSnapshotAsync("snapshot.png");

        Assert.Equal(ErrorCode.PlayerNotInitialized, open.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, play.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, pause.ErrorCode);
        Assert.True(stop.IsSuccess);
        Assert.Equal(ErrorCode.PlayerNotInitialized, seek.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, volume.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, mute.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, rate.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, tracks.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, audioTrack.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, subtitleTrack.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, subtitle.ErrorCode);
        Assert.Equal(ErrorCode.PlayerNotInitialized, snapshot.ErrorCode);
    }

    [Fact]
    public async Task OpenAsync_WhitespacePath_ReturnsInvalidPath()
    {
        await using var service = CreateService();

        var result = await service.OpenAsync(
            new OpenMediaRequest("   ", TimeSpan.Zero, false, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidPath, result.ErrorCode);
    }

    [Fact]
    public async Task PlayPauseAndSeekWithoutMedia_ReturnInvalidState()
    {
        await using var service = CreateService();

        var play = await service.PlayAsync();
        var pause = await service.PauseAsync();
        var seek = await service.SeekAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ErrorCode.InvalidState, play.ErrorCode);
        Assert.Equal(ErrorCode.InvalidState, pause.ErrorCode);
        Assert.Equal(ErrorCode.InvalidState, seek.ErrorCode);
    }

    [Fact]
    public async Task SetVolumeMuteAndRate_UpdateSnapshot()
    {
        await using var service = CreateService();

        var volume = await service.SetVolumeAsync(-10);
        var mute = await service.SetMuteAsync(true);
        var rate = await service.SetRateAsync(4.0);
        var snapshot = service.Snapshot;

        Assert.True(volume.IsSuccess);
        Assert.True(mute.IsSuccess);
        Assert.True(rate.IsSuccess);
        Assert.Equal(0, snapshot.Volume);
        Assert.True(snapshot.IsMuted);
        Assert.Equal(4.0, snapshot.Rate);
    }

    [Fact]
    public async Task GetTracksAsync_NoMedia_ReturnsEmptyCollections()
    {
        await using var service = CreateService();

        var audio = await service.GetTracksAsync(PlayerTrackType.Audio);
        var subtitles = await service.GetTracksAsync(PlayerTrackType.Subtitle);

        Assert.True(audio.IsSuccess);
        Assert.Empty(audio.Value!);
        Assert.True(subtitles.IsSuccess);
        Assert.Empty(subtitles.Value!);
    }

    [Fact]
    public async Task StopAsync_AfterOpen_TransitionsToStopped()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            var stopped = await service.StopAsync();

            Assert.True(opened.IsSuccess);
            Assert.True(stopped.IsSuccess);
            Assert.Equal(PlaybackState.Stopped, service.Snapshot.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandlePlayingWithStartPosition_TransitionsToPlaying()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(
                new OpenMediaRequest(
                    path,
                    TimeSpan.FromSeconds(10),
                    false,
                    null,
                    null));
            Assert.True(opened.IsSuccess);

            service.HandleMediaEvent(opened.Value!.MediaSessionId, PlaybackState.Playing);

            Assert.Equal(PlaybackState.Playing, service.Snapshot.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleEndReached_AfterPlaying_PublishesEndEvent()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            var sessionId = opened.Value!.MediaSessionId;
            service.HandleMediaEvent(sessionId, PlaybackState.Playing);
            EndReachedEventArgs? args = null;
            service.EndReached += (_, eventArgs) => args = eventArgs;

            service.HandleEndReached(sessionId);

            Assert.Equal(PlaybackState.Ended, service.Snapshot.State);
            Assert.NotNull(args);
            Assert.Equal(Path.GetFullPath(path), args.FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleMediaError_AfterOpen_PublishesFailure()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            PlaybackFailedEventArgs? args = null;
            service.PlaybackFailed += (_, eventArgs) => args = eventArgs;

            service.HandleMediaError(opened.Value!.MediaSessionId);

            Assert.Equal(PlaybackState.Error, service.Snapshot.State);
            Assert.NotNull(args);
            Assert.Equal(ErrorCode.UnsupportedMedia, args.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleLengthChanged_WhenParsedSubscriberThrows_StillRaisesTracksChanged()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            var tracksRaised = false;
            service.MediaParsed += (_, _) => throw new InvalidOperationException("consumer failed");
            service.TracksChanged += (_, _) => tracksRaised = true;

            service.HandleLengthChanged(opened.Value!.MediaSessionId, 1234);

            Assert.True(tracksRaised);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HandleMediaParsedChanged_WhenTrackSubscriberThrows_IsContained()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);
            service.TracksChanged += (_, _) => throw new InvalidOperationException("track consumer failed");

            var exception = Record.Exception(
                () => service.HandleMediaParsedChanged(
                    opened.Value!.MediaSessionId,
                    MediaParsedStatus.Done));

            Assert.Null(exception);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WithExternalSubtitle_ReturnsOpenResult()
    {
        var path = CreateTempMediaFile();
        var subtitlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        File.WriteAllText(subtitlePath, "1\n00:00:00,000 --> 00:00:01,000\nhello\n");
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(
                new OpenMediaRequest(path, TimeSpan.Zero, false, subtitlePath, null));

            Assert.True(opened.IsSuccess);
        }
        finally
        {
            File.Delete(path);
            File.Delete(subtitlePath);
        }
    }

    [Fact]
    public async Task OpenAsync_WithInvalidExternalSubtitle_StillOpensMedia()
    {
        var path = CreateTempMediaFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(
                new OpenMediaRequest(path, TimeSpan.Zero, false, "\0", null));

            Assert.True(opened.IsSuccess);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_WithAutoPlay_UsesOpenPlaybackPath()
    {
        var path = CreateTempWavFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(
                new OpenMediaRequest(path, TimeSpan.Zero, true, null, null));

            Assert.True(opened.IsSuccess);
            Assert.True(opened.Value!.AutoPlay);
        }
        finally
        {
            await service.DisposeAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PlayAsync_AfterWavOpen_ReturnsSuccess()
    {
        var path = CreateTempWavFile();
        await using var service = CreateService();

        try
        {
            var opened = await service.OpenAsync(CreateRequest(path));
            Assert.True(opened.IsSuccess);

            var played = await service.PlayAsync();

            Assert.True(played.IsSuccess);
        }
        finally
        {
            await service.DisposeAsync();
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

    private static string CreateTempWavFile()
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate;
        const int dataSize = sampleCount * channels * bitsPerSample / 8;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((short)0);
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
