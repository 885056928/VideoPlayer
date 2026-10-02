using System.IO;
using System.Text.Json;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Settings;

public sealed class SettingsServiceConcurrencyTests
{
    [Fact]
    public async Task SaveImmediateAsync_AfterReplacementFails_ReleasesGateAndAllowsNextSave()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var files = new HoldingSettingsFileOperations { FailNextPromotion = true };
        files.ReleaseFirstWrite.TrySetResult();
        try
        {
            var paths = new AppPaths(root);
            Assert.True((await new SettingsService(paths).SaveImmediateAsync()).IsSuccess);
            var service = new SettingsService(paths, files);
            service.Current.Player.DefaultVolume = 10;

            var failed = await service.SaveImmediateAsync();

            Assert.Equal(ErrorCode.ConfigInvalid, failed.ErrorCode);
            Assert.IsType<IOException>(failed.Exception);
            Assert.False(File.Exists(paths.ConfigPath + ".tmp"));
            var original = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigPath));
            Assert.Equal(80, original!.Player.DefaultVolume);

            service.Current.Player.DefaultVolume = 20;
            var next = await service.SaveImmediateAsync().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(next.IsSuccess, next.Exception?.ToString());
            var main = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigPath));
            var backup = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigBackupPath));
            Assert.Equal(20, main!.Player.DefaultVolume);
            Assert.Equal(80, backup!.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenSavesOverlap_PersistsLatestSettingsAndPreviousBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var files = new HoldingSettingsFileOperations();
        Task<ServiceResult>? firstSave = null;
        Task<ServiceResult>? secondSave = null;
        try
        {
            var paths = new AppPaths(root);
            Assert.True((await new SettingsService(paths).SaveImmediateAsync()).IsSuccess);
            var service = new SettingsService(paths, files);
            service.Current.Player.DefaultVolume = 10;
            firstSave = service.SaveAsync();
            await files.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            service.Current.Player.DefaultVolume = 20;
            secondSave = service.SaveImmediateAsync();
            files.ReleaseFirstWrite.TrySetResult();
            var results = await Task.WhenAll(firstSave, secondSave).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.All(results, result => Assert.True(result.IsSuccess, result.Exception?.ToString()));
            var main = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigPath));
            var backup = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigBackupPath));
            Assert.Equal(20, main!.Player.DefaultVolume);
            Assert.Equal(10, backup!.Player.DefaultVolume);
            Assert.False(File.Exists(paths.ConfigPath + ".tmp"));
        }
        finally
        {
            files.ReleaseFirstWrite.TrySetResult();
            if (firstSave is not null) await firstSave;
            if (secondSave is not null) await secondSave;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveImmediateAsync_WhenAnotherSaveIsCancelled_PreservesActiveTemporaryFile(
        bool cancelBeforeRequest)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var files = new HoldingSettingsFileOperations();
        Task<ServiceResult>? activeSave = null;
        try
        {
            var paths = new AppPaths(root);
            Assert.True((await new SettingsService(paths).SaveImmediateAsync()).IsSuccess);
            var service = new SettingsService(paths, files);
            service.Current.Player.DefaultVolume = 10;
            activeSave = service.SaveAsync();
            await files.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource();
            if (cancelBeforeRequest) cancellation.Cancel();

            var cancelledSave = service.SaveImmediateAsync(cancellation.Token);
            cancellation.Cancel();
            var cancelled = await cancelledSave.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(ErrorCode.Cancelled, cancelled.ErrorCode);
            Assert.True(File.Exists(paths.ConfigPath + ".tmp"));
            files.ReleaseFirstWrite.TrySetResult();
            Assert.True((await activeSave.WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            var main = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(paths.ConfigPath));
            Assert.Equal(10, main!.Player.DefaultVolume);
        }
        finally
        {
            files.ReleaseFirstWrite.TrySetResult();
            if (activeSave is not null) await activeSave;
            Directory.Delete(root, true);
        }
    }

    private sealed class HoldingSettingsFileOperations : ISettingsFileOperations
    {
        private readonly SettingsFileOperations _inner = new();
        private int _writeCount;
        public bool FailNextPromotion { get; set; }
        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FileExists(string path) => _inner.FileExists(path);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
            => _inner.ReadAllTextAsync(path, cancellationToken);

        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 实际写入临时文件后暂缓首次返回，精确复现另一保存覆盖或删除该文件的窗口。
            File.WriteAllText(path, contents);
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                FirstWriteStarted.TrySetResult();
                return ReleaseFirstWrite.Task.WaitAsync(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public void Replace(string sourcePath, string destinationPath, string? backupPath, bool ignoreMetadataErrors)
        {
            if (FailNextPromotion)
            {
                FailNextPromotion = false;
                throw new IOException("Forced replacement failure.");
            }

            _inner.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors);
        }

        public void Move(string sourcePath, string destinationPath) => _inner.Move(sourcePath, destinationPath);

        public void Delete(string path) => _inner.Delete(path);
    }
}
