using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
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
}
