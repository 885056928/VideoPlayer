using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;

namespace VideoPlayer;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.CreateDefault();
        paths.EnsureCoreDirectories();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "yyyy-MM-dd.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        try
        {
            _services = CompositionRoot.Create(paths, Log.Logger);
            var settings = _services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();

            var window = new MainWindow(settings.Current);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application startup failed.");
            MessageBox.Show("VideoPlayer 启动失败。", "VideoPlayer");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
