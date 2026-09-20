using System.IO;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class MediaInfoServiceCoverageTests
{
    [Fact]
    public async Task GetCurrentMediaInfoAsync_Cancelled_ReturnsCancelled()
    {
        var service = new MediaInfoService(new PlayerViewModelFixture(1).Player);
        var snapshot = CreateSnapshot("movie.mp4");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.GetCurrentMediaInfoAsync(
            snapshot,
            [],
            cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentMediaInfoAsync_ExistingFile_ReportsFileMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        await File.WriteAllBytesAsync(path, new byte[42]);
        var service = new MediaInfoService(new PlayerViewModelFixture(1).Player);

        try
        {
            var result = await service.GetCurrentMediaInfoAsync(
                CreateSnapshot(path),
                []);

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.GetFileName(path), result.Value!.FileName);
            Assert.Equal(42, result.Value.FileSizeBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetCurrentMediaInfoAsync_RootPath_UsesUnknownFileName()
    {
        var service = new MediaInfoService(new PlayerViewModelFixture(1).Player);

        var result = await service.GetCurrentMediaInfoAsync(
            CreateSnapshot(Path.GetPathRoot(Path.GetTempPath())),
            []);

        Assert.True(result.IsSuccess);
        Assert.Equal("未知", result.Value!.FileName);
        Assert.Equal(0, result.Value.FileSizeBytes);
    }

    [Fact]
    public async Task GetCurrentMediaInfoAsync_WavMedia_ReportsDurationAndAudioTrack()
    {
        var path = CreateTempWavFile();
        await using var player = new PlayerService(
            new ImmediateUiDispatcher(),
            new LoggerConfiguration().CreateLogger(),
            ["--vout=dummy"]);

        try
        {
            var opened = await player.OpenAsync(
                new OpenMediaRequest(path, TimeSpan.Zero, false, null, null));
            Assert.True(opened.IsSuccess);
            var played = await player.PlayAsync();
            Assert.True(played.IsSuccess);
            var service = new MediaInfoService(player);
            ServiceResult<VideoInfo>? result = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            do
            {
                result = await service.GetCurrentMediaInfoAsync(player.Snapshot, []);
                if (result.Value?.Channels is not null)
                {
                    break;
                }

                await Task.Delay(50);
            }
            while (DateTimeOffset.UtcNow < deadline);

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
            Assert.Equal(Path.GetFileName(path), result.Value!.FileName);
            Assert.Equal(1, result.Value.Channels);
            Assert.Equal(8000, result.Value.SampleRate);
            Assert.True(result.Value.Duration > TimeSpan.Zero);
        }
        finally
        {
            await player.DisposeAsync();
            File.Delete(path);
        }
    }

    private static PlaybackSnapshot CreateSnapshot(string? mediaPath)
    {
        return new PlaybackSnapshot(
            1,
            PlaybackState.Paused,
            mediaPath,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);
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