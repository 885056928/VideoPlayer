using System.IO;
using System.Text.Json;
using VideoPlayer.Enums;
using VideoPlayer.Configuration;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Settings;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SaveImmediateAsync_CreatesBackupWhenMainExists()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            var service = new SettingsService(paths);
            await service.SaveImmediateAsync();
            service.Current.Player.DefaultVolume = 55;

            var result = await service.SaveImmediateAsync();

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(paths.ConfigPath));
            Assert.True(File.Exists(paths.ConfigBackupPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesValidBackupWhenMainIsCorrupt()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var backup = AppSettings.CreateDefault();
            backup.Player.DefaultVolume = 23;
            await File.WriteAllTextAsync(
                paths.ConfigBackupPath,
                JsonSerializer.Serialize(backup));
            var service = new SettingsService(paths);

            var result = await service.LoadAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(23, service.Current.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsCancelledWhenTokenIsAlreadyCanceled()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            var service = new SettingsService(paths);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await service.LoadAsync(cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveImmediateAsync_ReturnsCancelledWhenTokenIsAlreadyCanceled()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            var service = new SettingsService(paths);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await service.SaveImmediateAsync(cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_RecoveryPreservesValidBackup()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var backup = AppSettings.CreateDefault();
            backup.Player.DefaultVolume = 23;
            await File.WriteAllTextAsync(
                paths.ConfigBackupPath,
                JsonSerializer.Serialize(backup));
            var service = new SettingsService(paths);

            var result = await service.LoadAsync();

            Assert.True(result.IsSuccess);
            var recoveredMain = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(paths.ConfigPath));
            var recoveredBackup = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(paths.ConfigBackupPath));
            Assert.NotNull(recoveredMain);
            Assert.NotNull(recoveredBackup);
            Assert.Equal(23, recoveredMain.Player.DefaultVolume);
            Assert.Equal(23, recoveredBackup.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsRegenerationFailureAndKeepsRecoveredSettings()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var backup = AppSettings.CreateDefault();
            backup.Player.DefaultVolume = 23;
            await File.WriteAllTextAsync(
                paths.ConfigBackupPath,
                JsonSerializer.Serialize(backup));
            var service = new SettingsService(
                paths,
                new ForcedPromotionFailureFileOperations());

            var result = await service.LoadAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.NotNull(result.Exception);
            Assert.Equal(23, service.Current.Player.DefaultVolume);
            var persistedBackup = JsonSerializer.Deserialize<AppSettings>(
                await File.ReadAllTextAsync(paths.ConfigBackupPath));
            Assert.NotNull(persistedBackup);
            Assert.Equal(23, persistedBackup.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SaveImmediateAsync_CleansUpTemporaryFileWhenReplacementFails()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            var initialService = new SettingsService(paths);
            await initialService.SaveImmediateAsync();
            var service = new SettingsService(
                paths,
                new ForcedPromotionFailureFileOperations());
            service.Current.Player.DefaultVolume = 55;

            var result = await service.SaveImmediateAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.False(File.Exists(paths.ConfigPath + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsRegenerationFailureAfterUsingDefaults()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            var service = new SettingsService(
                paths,
                new ForcedPromotionFailureFileOperations());

            var result = await service.LoadAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ConfigInvalid, result.ErrorCode);
            Assert.Equal(80, service.Current.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public async Task LoadAsync_NewerSchema_UsesDefaultsAndSucceeds()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            var newer = AppSettings.CreateDefault();
            newer.SchemaVersion = AppSettings.CurrentSchemaVersion + 1;
            newer.Player.DefaultVolume = 12;
            await File.WriteAllTextAsync(
                paths.ConfigPath,
                JsonSerializer.Serialize(newer));
            var service = new SettingsService(paths);

            var result = await service.LoadAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(AppSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
            Assert.Equal(80, service.Current.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_NewerBackupSchema_UsesDefaults()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var newer = AppSettings.CreateDefault();
            newer.SchemaVersion = AppSettings.CurrentSchemaVersion + 1;
            newer.Player.DefaultVolume = 11;
            await File.WriteAllTextAsync(
                paths.ConfigBackupPath,
                JsonSerializer.Serialize(newer));
            var service = new SettingsService(paths);

            var result = await service.LoadAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(80, service.Current.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private sealed class ForcedPromotionFailureFileOperations : ISettingsFileOperations
    {
        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public Task<string> ReadAllTextAsync(
            string path,
            CancellationToken cancellationToken)
        {
            return File.ReadAllTextAsync(path, cancellationToken);
        }

        public Task WriteAllTextAsync(
            string path,
            string contents,
            CancellationToken cancellationToken)
        {
            return File.WriteAllTextAsync(path, contents, cancellationToken);
        }

        public void Replace(
            string sourcePath,
            string destinationPath,
            string? backupPath,
            bool ignoreMetadataErrors)
        {
            throw new IOException("Forced replacement failure.");
        }

        public void Move(string sourcePath, string destinationPath)
        {
            throw new IOException("Forced promotion failure.");
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }
    }
}