using System.IO;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaylistServiceConcurrencyTests
{
    [Fact]
    public async Task ConcurrentAddRemoveAndNavigation_RemainsConsistent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = Enumerable.Range(0, 40)
                .Select(index => Path.Combine(root, $"video-{index:D2}.mp4"))
                .ToArray();
            foreach (var file in files)
            {
                File.WriteAllText(file, string.Empty);
            }

            var service = new PlaylistService(new FileService());
            var addTasks = files.Select(
                file => service.AddFilesAsync([file], recursive: false));
            await Task.WhenAll(addTasks);
            Assert.Equal(files.Length, service.Items.Count);

            var operations = new List<Task>();
            for (var index = 0; index < files.Length; index += 2)
            {
                operations.Add(service.RemoveAsync(files[index]));
            }

            for (var index = 0; index < 100; index++)
            {
                operations.Add(Task.Run(() =>
                {
                    service.GetNext();
                    service.GetPrevious();
                    service.SetCurrent(files[(index % files.Length)]);
                }));
            }

            await Task.WhenAll(operations);

            Assert.Equal(files.Length / 2, service.Items.Count);
            Assert.Equal(service.Items.Count, service.Items.Distinct().Count());
            Assert.InRange(service.CurrentIndex, -1, service.Items.Count - 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
