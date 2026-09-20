using System.IO;
using System.Windows.Media.Imaging;
using VideoPlayer.Converters;
using Xunit;

namespace VideoPlayer.Tests.Unit.Converters;

public sealed class PreviewPathToImageConverterTests
{
    [StaFact]
    public void Convert_LoadsFrozenPreviewImage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        File.WriteAllBytes(
            path,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z9ZkAAAAASUVORK5CYII="));
        try
        {
            var converter = new PreviewPathToImageConverter();

            var image = Assert.IsType<BitmapImage>(
                converter.Convert(path, typeof(BitmapImage), null!, System.Globalization.CultureInfo.InvariantCulture));

            Assert.True(image.IsFrozen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [StaFact]
    public void Convert_ReturnsNullForInvalidInput()
    {
        var converter = new PreviewPathToImageConverter();

        Assert.Null(converter.Convert(
            42,
            typeof(BitmapImage),
            null!,
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png"),
            typeof(BitmapImage),
            null!,
            System.Globalization.CultureInfo.InvariantCulture));
    }
}