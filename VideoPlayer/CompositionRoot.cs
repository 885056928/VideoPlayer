using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Database;
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
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddDbContextFactory<VideoPlayerDbContext>(options =>
            options.UseSqlite("Data Source=" + paths.DatabasePath));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<ISubtitleService, SubtitleService>();
        services.AddSingleton<IMediaInfoService, MediaInfoService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUiDispatcher>(_ =>
            new WpfUiDispatcher(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IPlayerService>(provider =>
            new PlayerService(
                provider.GetRequiredService<IUiDispatcher>(),
                provider.GetRequiredService<ILogger>()));
        services.AddSingleton<PlayerViewModel>(provider =>
            new PlayerViewModel(
                provider.GetRequiredService<IPlayerService>(),
                provider.GetRequiredService<IUiDispatcher>(),
                provider.GetRequiredService<IPlaylistService>(),
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<IDialogService>(),
                provider.GetRequiredService<ISubtitleService>(),
                provider.GetRequiredService<IHistoryService>(),
                provider.GetRequiredService<IScreenshotService>()));
        services.AddSingleton<VideoInfoViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
