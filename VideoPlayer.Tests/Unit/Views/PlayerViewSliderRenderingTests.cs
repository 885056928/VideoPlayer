using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class PlayerViewSliderRenderingTests
{
    [WpfTheory]
    [InlineData("MediaProgressSliderStyle", 320, 0, 1)]
    [InlineData("MediaProgressSliderStyle", 320, 50, 1)]
    [InlineData("MediaProgressSliderStyle", 320, 100, 1)]
    [InlineData("MediaProgressSliderStyle", 320, 50, 1.5)]
    [InlineData("MediaVolumeSliderStyle", 100, 0, 1)]
    [InlineData("MediaVolumeSliderStyle", 100, 50, 1)]
    [InlineData("MediaVolumeSliderStyle", 100, 100, 1)]
    [InlineData("MediaVolumeSliderStyle", 100, 50, 1.5)]
    public void Slider_TrackJoinsThumbWithoutExposedRoundedEnds(
        string styleKey,
        double width,
        double value,
        double dpiScale)
    {
        using var view = new PlayerView();
        var slider = new Slider
        {
            Style = Assert.IsType<Style>(view.Resources[styleKey]),
            Minimum = 0,
            Maximum = 100,
            Value = value,
            Width = width
        };
        slider.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/VideoPlayer;component/Resources/Styles/Colors.xaml")
        });
        slider.Measure(new Size(width, 24));
        slider.Arrange(new Rect(0, 0, width, 24));
        slider.UpdateLayout();
        var track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
        var thumbOrigin = track.Thumb.TranslatePoint(new Point(), slider);
        var thumbCenter = new Point(
            thumbOrigin.X + track.Thumb.ActualWidth / 2,
            thumbOrigin.Y + track.Thumb.ActualHeight / 2);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpiScale),
            (int)Math.Ceiling(slider.ActualHeight * dpiScale),
            96 * dpiScale,
            96 * dpiScale,
            PixelFormats.Pbgra32);
        bitmap.Render(slider);

        // 检查真实渲染的接缝：圆点外应保持完整轨道，圆点中心不能被延伸的轨道盖住。
        if (value > 0)
        {
            AssertPixelColor(bitmap, thumbOrigin.X - 1, thumbCenter.Y - 2, dpiScale, Color.FromRgb(0x4C, 0x9A, 0xFF));
        }

        if (value < 100)
        {
            AssertPixelColor(bitmap, thumbOrigin.X + track.Thumb.ActualWidth + 1, thumbCenter.Y - 2, dpiScale, Color.FromRgb(0x33, 0x33, 0x33));
        }

        AssertPixelColor(bitmap, thumbCenter.X, thumbCenter.Y, dpiScale, Colors.White);
    }

    private static void AssertPixelColor(
        BitmapSource bitmap,
        double x,
        double y,
        double dpiScale,
        Color expected)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(x * dpiScale), (int)(y * dpiScale), 1, 1), pixel, 4, 0);
        Assert.Equal(new[] { expected.B, expected.G, expected.R, expected.A }, pixel);
    }
}
