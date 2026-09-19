using System.IO;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
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

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
