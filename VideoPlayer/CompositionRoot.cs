using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;

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
        services.AddSingleton<IUiDispatcher>(_ =>
            new WpfUiDispatcher(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IPlayerService>(provider =>
            new PlayerService(
                provider.GetRequiredService<IUiDispatcher>(),
                provider.GetRequiredService<ILogger>()));
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
