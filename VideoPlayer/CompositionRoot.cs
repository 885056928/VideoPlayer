using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using VideoPlayer.Services;

namespace VideoPlayer;

public static class CompositionRoot
{
    public static ServiceProvider Create(AppPaths paths, ILogger logger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(logger);
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
