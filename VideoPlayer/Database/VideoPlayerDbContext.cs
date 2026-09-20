using Microsoft.EntityFrameworkCore;
using VideoPlayer.Models;

namespace VideoPlayer.Database;

public sealed class VideoPlayerDbContext : DbContext
{
    public VideoPlayerDbContext(DbContextOptions<VideoPlayerDbContext> options)
        : base(options)
    {
    }

    public DbSet<PlaybackHistory> PlaybackHistory => Set<PlaybackHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlaybackHistory>();
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => x.PathKey).IsUnique();
        entity.Property(x => x.FilePath).IsRequired();
        entity.Property(x => x.PathKey).IsRequired();
        entity.Property(x => x.FileName).IsRequired();
    }
}
