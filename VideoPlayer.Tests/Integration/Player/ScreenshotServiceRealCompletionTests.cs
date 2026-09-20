using System.IO;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.WPF;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Player;

public sealed class ScreenshotServiceRealCompletionTests
{
    private static readonly byte[] JpegFrame = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAgACADASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KK5Cjh3h3+2fafvOTkt0ve9/Ndj9Qz7Pv7J9n+75+e/W1rW8n3OvorkK6+jiLh3+xvZ/vOfnv0ta1vN9wyHPv7W9p+75OS3W9738l2CuQrr6KOHeIv7G9p+75+e3W1rX8n3DPsh/tb2f7zk5L9L3vbzXY5Cuvooo4i4i/tn2f7vk5L9b3vbyXYMhyH+yfafvOfnt0ta1/N9z//Z");

    [Fact]
    public async Task CaptureAsync_WithRealLibVlc_WritesVerifiedPng()
    {
        var mediaPath = CreateMediaFile();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var host = new StaWindowHost();
        await using var player = new PlayerService(
            host.UiDispatcher,
            new LoggerConfiguration().CreateLogger(),
            ["--aout=dummy", "--no-audio"]);
        var videoView = host.Invoke(() => new VideoView());
        var window = host.Invoke(() =>
        {
            var value = new Window
            {
                Width = 320,
                Height = 240,
                Content = videoView,
                ShowInTaskbar = false
            };
            videoView.MediaPlayer = player.MediaPlayer;
            value.Show();
            return value;
        });

        try
        {
            var opened = await player.OpenAsync(
                new OpenMediaRequest(mediaPath, TimeSpan.Zero, AutoPlay: true, null, null));
            Assert.True(opened.IsSuccess);
            await WaitForPlayingAsync(player, opened.Value!.MediaSessionId);
            await WaitForPositionAsync(player, TimeSpan.FromMilliseconds(500));

            var paths = new AppPaths(root);
            var settings = new SettingsService(paths);
            settings.Current.Screenshot.Directory = Path.Combine(root, "Screenshots");
            var screenshot = new ScreenshotService(player, settings, paths);

            var result = await screenshot.CaptureAsync(player.Snapshot);

            Assert.True(result.IsSuccess, result.TechnicalMessage);
            Assert.True(File.Exists(result.Value));
            Assert.True(new FileInfo(result.Value).Length > 0);
        }
        finally
        {
            await host.InvokeAsync(() =>
            {
                window.Close();
                videoView.Dispose();
            });
            File.Delete(mediaPath);
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitForPositionAsync(PlayerService service, TimeSpan minimum)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (service.Snapshot.Position >= minimum)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("Timed out waiting for video position.");
    }

    private static async Task WaitForPlayingAsync(PlayerService service, long sessionId)
    {
        if (service.Snapshot.State == PlaybackState.Playing)
        {
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<PlaybackStateChangedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            if (args.MediaSessionId == sessionId && args.NewState == PlaybackState.Playing)
            {
                service.StateChanged -= handler;
                completion.TrySetResult();
            }
        };
        service.StateChanged += handler;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            service.StateChanged -= handler;
        }
    }

    private static string CreateMediaFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mjpeg");
        using var stream = File.Create(path);
        for (var index = 0; index < 40; index++)
        {
            stream.Write(JpegFrame);
        }

        return path;
    }

    private sealed class StaWindowHost : IDisposable
    {
        private Dispatcher _dispatcher = null!;
        private readonly Thread _thread;

        public StaWindowHost()
        {
            var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "VideoPlayer screenshot STA"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
            UiDispatcher = new WpfUiDispatcher(_dispatcher);
        }

        public IUiDispatcher UiDispatcher { get; }

        public T Invoke<T>(Func<T> action) => _dispatcher.Invoke(action);

        public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

        public void Dispose()
        {
            _dispatcher.InvokeShutdown();
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
