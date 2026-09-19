using System.IO;
using System.Text.Json;
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

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
