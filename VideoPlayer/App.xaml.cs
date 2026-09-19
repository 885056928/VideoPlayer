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
        try
        {
            InitializeFallbackLogger();
        }
        catch
        {
            ShowStartupFailureSafely();
            ShutdownSafely();
            return;
        }

        try
        {
            base.OnStartup(e);

            var paths = AppPaths.CreateDefault();
            paths.EnsureCoreDirectories();
            InitializeFileLogger(paths);

            _services = CompositionRoot.Create(paths, Log.Logger);
            var settings = _services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();

            var window = new MainWindow(settings.Current);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            LogFatalSafely(exception);
            ShowStartupFailureSafely();
            ShutdownSafely();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void InitializeFallbackLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .CreateLogger();
    }

    private static void InitializeFileLogger(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "yyyy-MM-dd.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }

    private static void LogFatalSafely(Exception exception)
    {
        try
        {
            Log.Fatal(exception, "Application startup failed.");
        }
        catch
        {
            // Fallback logging must never cause another startup failure.
        }
    }

    private static void ShowStartupFailureSafely()
    {
        try
        {
            MessageBox.Show("VideoPlayer 启动失败。", "VideoPlayer");
        }
        catch
        {
            // Failure presentation must not escape as an unhandled exception.
        }
    }

    private void ShutdownSafely()
    {
        try
        {
            Shutdown(1);
        }
        catch
        {
            // The application is already failing to start; do not surface another exception.
        }
    }
}
