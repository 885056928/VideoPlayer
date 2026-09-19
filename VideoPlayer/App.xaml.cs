using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using VideoPlayer.ViewModels;

namespace VideoPlayer;

public partial class App : Application
{
    private static readonly TimeSpan ServiceDisposalTimeout = TimeSpan.FromSeconds(5);
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

            var viewModel = _services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(viewModel);
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
        DisposeServicesSafely();
        base.OnExit(e);
    }

    private void DisposeServicesSafely()
    {
        var services = Interlocked.Exchange(ref _services, null);
        if (services is null)
        {
            CloseLoggerSafely();
            return;
        }

        var closeLogger = true;
        try
        {
            var disposeTask = Task.Run(
                async () => await services.DisposeAsync().ConfigureAwait(false));

            if (!disposeTask.Wait(ServiceDisposalTimeout))
            {
                closeLogger = false;
                Log.Warning(
                    "Service provider disposal timed out after {Timeout}.",
                    ServiceDisposalTimeout);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Service provider disposal failed.");
        }
        finally
        {
            if (closeLogger)
            {
                CloseLoggerSafely();
            }
        }
    }

    private static void CloseLoggerSafely()
    {
        try
        {
            Log.CloseAndFlush();
        }
        catch
        {
            // Shutdown must remain best-effort and non-throwing.
        }
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
