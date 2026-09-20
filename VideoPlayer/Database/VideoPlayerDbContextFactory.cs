using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VideoPlayer.Configuration;

namespace VideoPlayer.Database;

public sealed class VideoPlayerDbContextFactory : IDesignTimeDbContextFactory<VideoPlayerDbContext>
{
    public VideoPlayerDbContext CreateDbContext(string[] args)
    {
        var paths = AppPaths.CreateDefault();
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite("Data Source=" + paths.DatabasePath)
            .Options;
        return new VideoPlayerDbContext(options);
    }
}
