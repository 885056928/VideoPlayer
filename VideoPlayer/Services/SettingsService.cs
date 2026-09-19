using System.IO;
using System.Text.Json;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    public async Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _paths.EnsureCoreDirectories();
            if (File.Exists(_paths.ConfigPath))
            {
                var loaded = await ReadAsync(_paths.ConfigPath, cancellationToken);
                if (loaded is not null)
                {
                    Current = SettingsValidator.Normalize(loaded);
                    return ServiceResult.Success();
                }
            }

            if (File.Exists(_paths.ConfigBackupPath))
            {
                var backup = await ReadAsync(_paths.ConfigBackupPath, cancellationToken);
                if (backup is not null)
                {
                    Current = SettingsValidator.Normalize(backup);
                    await SaveImmediateAsync(cancellationToken);
                    return ServiceResult.Success();
                }
            }

            Current = AppSettings.CreateDefault();
            await SaveImmediateAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (Exception exception)
        {
            Current = AppSettings.CreateDefault();
            return ServiceResult.Failure(
                ErrorCode.ConfigInvalid,
                "配置文件读取失败，已使用默认设置。",
                _paths.ConfigPath,
                exception);
        }
    }

    public Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        return SaveImmediateAsync(cancellationToken);
    }

    public async Task<ServiceResult> SaveImmediateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _paths.EnsureCoreDirectories();
            Current = SettingsValidator.Normalize(Current);
            var tempPath = _paths.ConfigPath + ".tmp";
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(Current, _jsonOptions),
                cancellationToken);

            if (File.Exists(_paths.ConfigPath))
            {
                File.Replace(
                    tempPath,
                    _paths.ConfigPath,
                    _paths.ConfigBackupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _paths.ConfigPath);
            }

            return ServiceResult.Success();
        }
        catch (Exception exception)
        {
            return ServiceResult.Failure(
                ErrorCode.ConfigInvalid,
                "配置保存失败。",
                _paths.ConfigPath,
                exception);
        }
    }

    private async Task<AppSettings?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
