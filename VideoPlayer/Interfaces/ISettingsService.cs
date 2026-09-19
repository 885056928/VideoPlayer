using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveImmediateAsync(CancellationToken cancellationToken = default);
}
