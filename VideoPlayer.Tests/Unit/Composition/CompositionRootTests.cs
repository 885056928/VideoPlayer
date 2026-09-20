using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.Composition;

public sealed class CompositionRootTests
{
    [Fact]
    public void Create_ResolvesFileAndSettingsServices()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            using var provider = CompositionRoot.Create(
                paths,
                new LoggerConfiguration().CreateLogger());

            Assert.NotNull(provider.GetRequiredService<IFileService>());
            Assert.NotNull(provider.GetRequiredService<ISubtitleService>());
            Assert.NotNull(provider.GetRequiredService<IHistoryService>());
            Assert.Same(
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<ISettingsService>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Create_RegistersScreenshotMediaInfoAndVideoInfoServices()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            using var provider = CompositionRoot.Create(
                paths,
                new LoggerConfiguration().CreateLogger());
            var registration = provider.GetRequiredService<IServiceProviderIsService>();

            Assert.True(registration.IsService(typeof(IScreenshotService)));
            Assert.True(registration.IsService(typeof(IMediaInfoService)));
            Assert.True(registration.IsService(typeof(VideoInfoViewModel)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
