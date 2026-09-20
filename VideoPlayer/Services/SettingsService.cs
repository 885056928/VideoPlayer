using System.IO;
using System.Text.Json;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _paths;
    private readonly ISettingsFileOperations _fileOperations;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SettingsService(AppPaths paths)
        : this(paths, new SettingsFileOperations())
    {
    }

    internal SettingsService(
        AppPaths paths,
        ISettingsFileOperations fileOperations)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileOperations);
        _paths = paths;
        _fileOperations = fileOperations;
    }

    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    public async Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _paths.EnsureCoreDirectories();
            if (_fileOperations.FileExists(_paths.ConfigPath))
            {
                var loaded = await ReadAsync(_paths.ConfigPath, cancellationToken);
                if (loaded is not null)
                {
                    if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
                    {
                        Log.Warning(
                            "Configuration schema {SchemaVersion} is newer than supported schema {SupportedSchemaVersion}; using defaults.",
                            loaded.SchemaVersion,
                            AppSettings.CurrentSchemaVersion);
                        Current = AppSettings.CreateDefault();
                        return ServiceResult.Success();
                    }

                    Current = SettingsValidator.Normalize(loaded);
                    return ServiceResult.Success();
                }
            }

            if (_fileOperations.FileExists(_paths.ConfigBackupPath))
            {
                var backup = await ReadAsync(_paths.ConfigBackupPath, cancellationToken);
                if (backup is not null)
                {
                    if (backup.SchemaVersion > AppSettings.CurrentSchemaVersion)
                    {
                        Log.Warning(
                            "Backup configuration schema {SchemaVersion} is newer than supported schema {SupportedSchemaVersion}; using defaults.",
                            backup.SchemaVersion,
                            AppSettings.CurrentSchemaVersion);
                        Current = AppSettings.CreateDefault();
                        return ServiceResult.Success();
                    }

                    Current = SettingsValidator.Normalize(backup);
                    return await SaveCoreAsync(createBackup: false, cancellationToken);
                }
            }

            Current = AppSettings.CreateDefault();
            return await SaveCoreAsync(createBackup: true, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult.Failure(
                ErrorCode.Cancelled,
                "配置读取已取消。",
                _paths.ConfigPath,
                exception);
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

    public Task<ServiceResult> SaveImmediateAsync(
        CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(createBackup: true, cancellationToken);
    }

    private async Task<ServiceResult> SaveCoreAsync(
        bool createBackup,
        CancellationToken cancellationToken)
    {
        var tempPath = _paths.ConfigPath + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _paths.EnsureCoreDirectories();
            Current = SettingsValidator.Normalize(Current);
            await _fileOperations.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(Current, _jsonOptions),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (_fileOperations.FileExists(_paths.ConfigPath))
            {
                _fileOperations.Replace(
                    tempPath,
                    _paths.ConfigPath,
                    createBackup ? _paths.ConfigBackupPath : null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                _fileOperations.Move(tempPath, _paths.ConfigPath);
            }

            return ServiceResult.Success();
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult.Failure(
                ErrorCode.Cancelled,
                "配置保存已取消。",
                _paths.ConfigPath,
                exception);
        }
        catch (Exception exception)
        {
            return ServiceResult.Failure(
                ErrorCode.ConfigInvalid,
                "配置保存失败。",
                _paths.ConfigPath,
                exception);
        }
        finally
        {
            try
            {
                _fileOperations.Delete(tempPath);
            }
            catch
            {
                // Cleanup is best-effort; the save result must remain non-fatal.
            }
        }
    }

    private async Task<AppSettings?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await _fileOperations.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}

internal interface ISettingsFileOperations
{
    bool FileExists(string path);

    Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken);

    Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken);

    void Replace(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors);

    void Move(string sourcePath, string destinationPath);

    void Delete(string path);
}

internal sealed class SettingsFileOperations : ISettingsFileOperations
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        return File.WriteAllTextAsync(path, contents, cancellationToken);
    }

    public void Replace(
        string sourcePath,
        string destinationPath,
        string? backupPath,
        bool ignoreMetadataErrors)
    {
        File.Replace(
            sourcePath,
            destinationPath,
            backupPath,
            ignoreMetadataErrors);
    }

    public void Move(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath);
    }

    public void Delete(string path)
    {
        File.Delete(path);
    }
}
