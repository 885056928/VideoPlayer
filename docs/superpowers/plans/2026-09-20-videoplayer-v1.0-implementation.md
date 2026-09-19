# VideoPlayer V1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a stable Windows local video player V1.0 with one WPF production project and one test project, following the V1.0 product specification and V1.1 engineering contract.

**Architecture:** WPF View binds only to ViewModels and the read-only `MediaPlayer` surface required by `VideoView`. `PlayerViewModel` is the sole orchestration layer; `PlayerService` exclusively owns LibVLC/MediaPlayer and serializes all player mutations. Persistence and file operations are asynchronous and isolated from playback.

**Tech Stack:** .NET 10, WPF, C# 14, LibVLCSharp.WPF 3.10.1, VideoLAN.LibVLC.Windows 3.0.23.1, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.12, EF Core SQLite 10.0.12, Serilog 4.4.0, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-20-videoplayer-v1.1-engineering-contract-design.md`

## Global Constraints

- Product baseline: `docs/specs/VideoPlayer-V1.0-Codex-开发规范书.md`.
- Engineering contract: `docs/superpowers/specs/2026-09-20-videoplayer-v1.1-engineering-contract-design.md`.
- Target framework: `net10.0-windows`.
- Platform: Windows x64 only; `PlatformTarget=x64`, `RuntimeIdentifier=win-x64`.
- Nullable: enabled and nullable warnings are build errors.
- Production projects: exactly one WPF project named `VideoPlayer`.
- Test projects: exactly one xUnit project named `VideoPlayer.Tests`.
- Publishing: `PublishSingleFile=false`, `PublishTrimmed=false`, `ReadyToRun=false`.
- Package versions are fixed by the V1.1 spec and `packages.lock.json`.
- View and ViewModel MUST NOT call LibVLC playback APIs directly.
- `PlayerService` MUST serialize all MediaPlayer mutations through one command gate.
- Every asynchronous media event MUST carry `MediaSessionId`; stale events MUST be ignored.
- VLC callbacks MUST NOT touch WPF, EF Core, dialogs, or blocking IO.
- Database/config failures MUST NOT prevent playback.
- Every task follows TDD: failing test, observed failure, minimal implementation, passing test, commit.

## Review Focus

1. Rapid next/open commands with late events from the previous media can change the wrong UI item; the current `MediaSessionId` must remain authoritative.
2. A locked, corrupt, or read-only SQLite database must not delay or stop playback; the app must enter a history-disabled state.
3. Paths containing Chinese, spaces, brackets, `#`, `&`, Unicode, or long paths must open correctly and map to one history record.
4. Unsupported media, absent video surfaces, or unavailable tracks must produce a typed result without corrupting `PlaybackState`.
5. Shutdown during Seek, history flush, or periodic UI refresh must not access disposed LibVLC objects or deadlock the UI thread.

---

## File Map

### Production

- `VideoPlayer/VideoPlayer.csproj`: target platform, packages, WPF, nullable and publish settings.
- `VideoPlayer/app.manifest`: long-path awareness and DPI declaration.
- `VideoPlayer/App.xaml`, `VideoPlayer/App.xaml.cs`: DI composition, startup and shutdown.
- `VideoPlayer/MainWindow.xaml`, `VideoPlayer/MainWindow.xaml.cs`: shell and keyboard bindings.
- `VideoPlayer/Views/PlayerView.xaml`, `PlayerView.xaml.cs`: VideoView and playback controls.
- `VideoPlayer/Views/PlaylistView.xaml`, `PlaylistView.xaml.cs`: virtualized playlist and drop target.
- `VideoPlayer/Views/VideoInfoView.xaml`, `VideoInfoView.xaml.cs`: media information.
- `VideoPlayer/ViewModels/MainViewModel.cs`: shell commands and dialog coordination.
- `VideoPlayer/ViewModels/PlayerViewModel.cs`: playback orchestration.
- `VideoPlayer/ViewModels/PlaylistViewModel.cs`: playlist observable projection.
- `VideoPlayer/ViewModels/VideoInfoViewModel.cs`: media information projection.
- `VideoPlayer/Models/*.cs`: enums, records, event args and service results.
- `VideoPlayer/Interfaces/*.cs`: service contracts.
- `VideoPlayer/Services/*.cs`: player, file, playlist, subtitle, history, media info, screenshot and settings services.
- `VideoPlayer/Database/VideoPlayerDbContext.cs`: EF Core model.
- `VideoPlayer/Database/DatabaseInitializer.cs`: migration and corruption recovery.
- `VideoPlayer/Configuration/AppPaths.cs`: application paths.
- `VideoPlayer/Helpers/PathHelper.cs`: path normalization, key creation and natural comparison.
- `VideoPlayer/Resources/Styles/*.xaml`: dark theme and control styles.

### Tests

- `VideoPlayer.Tests/VideoPlayer.Tests.csproj`: xUnit and project reference.
- `VideoPlayer.Tests/Unit/*.cs`: pure unit tests.
- `VideoPlayer.Tests/Integration/*.cs`: SQLite, filesystem and LibVLC integration tests.
- `VideoPlayer.Tests/Fakes/FakePlayerService.cs`: deterministic player event source.
- `VideoPlayer.Tests/Fixtures/TestMedia/`: short MP4 fixture and license notes.

---

### Task 1: Bootstrap the Solution and Establish Path Utilities

**Files:**
- Create: `global.json`
- Create: `VideoPlayer.sln`
- Create: `VideoPlayer/VideoPlayer.csproj`
- Create: `VideoPlayer/Helpers/PathHelper.cs`
- Create: `VideoPlayer.Tests/VideoPlayer.Tests.csproj`
- Create: `VideoPlayer.Tests/Unit/Helpers/PathHelperTests.cs`
- Create: `packages.lock.json` through restore

**Interfaces:**
- Consumes: none.
- Produces:
  - `PathHelper.NormalizePath(string) -> string`
  - `PathHelper.CreatePathKey(string) -> string`
  - test project reference to `VideoPlayer`.

- [ ] **Step 1: Scaffold the solution and projects**

Run:

```powershell
dotnet new sln -n VideoPlayer
dotnet new wpf -n VideoPlayer -f net10.0
dotnet new xunit -n VideoPlayer.Tests -f net10.0
dotnet sln VideoPlayer.sln add VideoPlayer/VideoPlayer.csproj
dotnet sln VideoPlayer.sln add VideoPlayer.Tests/VideoPlayer.Tests.csproj
dotnet add VideoPlayer.Tests/VideoPlayer.Tests.csproj reference VideoPlayer/VideoPlayer.csproj
```

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Configure `VideoPlayer/VideoPlayer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PlatformTarget>x64</PlatformTarget>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <WarningsAsErrors>nullable</WarningsAsErrors>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
    <ReadyToRun>false</ReadyToRun>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
</Project>
```

Configure `VideoPlayer.Tests/VideoPlayer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PlatformTarget>x64</PlatformTarget>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <WarningsAsErrors>nullable</WarningsAsErrors>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="10.0.1">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Xunit.StaFact" Version="1.2.69" />
    <ProjectReference Include="..\VideoPlayer\VideoPlayer.csproj" />
  </ItemGroup>
</Project>
```

Create `VideoPlayer/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: Write the failing PathHelper tests**

Create `VideoPlayer.Tests/Unit/Helpers/PathHelperTests.cs`:

```csharp
using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class PathHelperTests
{
    [Fact]
    public void NormalizePath_ReturnsAbsolutePath()
    {
        var relative = Path.Combine("media", "movie.mp4");

        var actual = PathHelper.NormalizePath(relative);

        Assert.Equal(Path.GetFullPath(relative), actual);
    }

    [Fact]
    public void CreatePathKey_IgnoresAsciiCase()
    {
        var upper = Path.Combine(Path.GetTempPath(), "Movie.MP4");
        var lower = Path.Combine(Path.GetTempPath(), "movie.mp4");

        Assert.Equal(PathHelper.CreatePathKey(upper), PathHelper.CreatePathKey(lower));
    }

    [Fact]
    public void NormalizePath_RejectsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => PathHelper.NormalizePath("   "));
    }
}
```

- [ ] **Step 3: Run the test and verify the expected failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PathHelperTests
```

Expected: FAIL because `VideoPlayer.Helpers.PathHelper` does not exist.

- [ ] **Step 4: Implement PathHelper**

Create `VideoPlayer/Helpers/PathHelper.cs`:

```csharp
using System.IO;

namespace VideoPlayer.Helpers;

public static class PathHelper
{
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public static string CreatePathKey(string path)
    {
        return NormalizePath(path).ToUpperInvariant();
    }
}
```

- [ ] **Step 5: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PathHelperTests
dotnet build VideoPlayer.sln -c Release
```

Expected: tests PASS and build succeeds with zero nullable errors.

- [ ] **Step 6: Commit**

```powershell
git add global.json VideoPlayer.sln VideoPlayer VideoPlayer.Tests
git commit -m "chore: bootstrap VideoPlayer solution and path helpers"
```

---

### Task 2: Define Core Enums, Records, and Service Results

**Files:**
- Create: `VideoPlayer/Enums/PlaybackState.cs`
- Create: `VideoPlayer/Enums/PlayerTrackType.cs`
- Create: `VideoPlayer/Enums/ErrorCode.cs`
- Create: `VideoPlayer/Models/PlaybackSnapshot.cs`
- Create: `VideoPlayer/Models/OpenMediaRequest.cs`
- Create: `VideoPlayer/Models/MediaSessionInfo.cs`
- Create: `VideoPlayer/Models/PlayerTrackInfo.cs`
- Create: `VideoPlayer/Models/ServiceResult.cs`
- Create: `VideoPlayer.Tests/Unit/Models/ServiceResultTests.cs`
- Create: `VideoPlayer.Tests/Unit/Models/PlaybackSnapshotTests.cs`

**Interfaces:**
- Consumes: none.
- Produces:
  - `PlaybackState`, `PlayerTrackType`, `ErrorCode`.
  - `PlaybackSnapshot`, `OpenMediaRequest`, `MediaSessionInfo`, `PlayerTrackInfo`.
  - `ServiceResult.Success()`, `ServiceResult.Failure(...)`.
  - `ServiceResult<T>.Success(T)`, `ServiceResult<T>.Failure(...)`.

- [ ] **Step 1: Write failing result and snapshot tests**

Create `VideoPlayer.Tests/Unit/Models/ServiceResultTests.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Unit.Models;

public sealed class ServiceResultTests
{
    [Fact]
    public void Failure_CarriesErrorCodeAndMessages()
    {
        var result = ServiceResult.Failure(
            ErrorCode.FileNotFound,
            "文件不存在。",
            "Missing file");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
        Assert.Equal("文件不存在。", result.UserMessage);
    }

    [Fact]
    public void GenericSuccess_CarriesValue()
    {
        var result = ServiceResult<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
    }
}
```

Create `VideoPlayer.Tests/Unit/Models/PlaybackSnapshotTests.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Unit.Models;

public sealed class PlaybackSnapshotTests
{
    [Fact]
    public void Constructor_PreservesAllPlaybackFields()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var snapshot = new PlaybackSnapshot(
            7,
            PlaybackState.Playing,
            @"C:\视频\测试.mp4",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            80,
            false,
            1.25,
            true,
            true,
            true,
            capturedAt);

        Assert.Equal(7, snapshot.MediaSessionId);
        Assert.Equal(PlaybackState.Playing, snapshot.State);
        Assert.Equal(1.25, snapshot.Rate);
        Assert.Equal(capturedAt, snapshot.CapturedAtUtc);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~ServiceResultTests|FullyQualifiedName~PlaybackSnapshotTests"
```

Expected: FAIL because the enums and records do not exist.

- [ ] **Step 3: Implement enums and records**

Create `VideoPlayer/Enums/PlaybackState.cs`:

```csharp
namespace VideoPlayer.Enums;

public enum PlaybackState
{
    None,
    Opening,
    Playing,
    Paused,
    Stopped,
    Ended,
    Error
}
```

Create `VideoPlayer/Enums/PlayerTrackType.cs`:

```csharp
namespace VideoPlayer.Enums;

public enum PlayerTrackType
{
    Audio,
    Subtitle
}
```

Create `VideoPlayer/Enums/ErrorCode.cs`:

```csharp
namespace VideoPlayer.Enums;

public enum ErrorCode
{
    None,
    InvalidPath,
    FileNotFound,
    NoPermission,
    UnsupportedMedia,
    PlayerNotInitialized,
    InvalidState,
    SeekNotAvailable,
    SubtitleNotFound,
    TrackNotFound,
    DatabaseUnavailable,
    ConfigInvalid,
    SnapshotFailed,
    Superseded,
    Cancelled,
    Unknown
}
```

Create `VideoPlayer/Models/PlaybackSnapshot.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record PlaybackSnapshot(
    long MediaSessionId,
    PlaybackState State,
    string? MediaPath,
    TimeSpan Position,
    TimeSpan Duration,
    int Volume,
    bool IsMuted,
    double Rate,
    bool IsSeekable,
    bool HasVideo,
    bool HasAudio,
    DateTimeOffset CapturedAtUtc);
```

Create `VideoPlayer/Models/OpenMediaRequest.cs`:

```csharp
namespace VideoPlayer.Models;

public sealed record OpenMediaRequest(
    string FilePath,
    TimeSpan StartPosition,
    bool AutoPlay,
    string? ExternalSubtitlePath,
    string? PreferredAudioLanguage);
```

Create `VideoPlayer/Models/MediaSessionInfo.cs`:

```csharp
namespace VideoPlayer.Models;

public sealed record MediaSessionInfo(
    long MediaSessionId,
    string FilePath,
    bool AutoPlay,
    TimeSpan RequestedStartPosition,
    DateTimeOffset CreatedAtUtc);
```

Create `VideoPlayer/Models/PlayerTrackInfo.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record PlayerTrackInfo(
    PlayerTrackType Type,
    int Id,
    string Name,
    string Language,
    bool IsSelected,
    bool IsExternal);
```

Create `VideoPlayer/Models/ServiceResult.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record ServiceResult(
    bool IsSuccess,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null)
{
    public static ServiceResult Success()
    {
        return new ServiceResult(true, ErrorCode.None, string.Empty, string.Empty);
    }

    public static ServiceResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string technicalMessage,
        Exception? exception = null)
    {
        return new ServiceResult(false, errorCode, userMessage, technicalMessage, exception);
    }
}

public sealed record ServiceResult<T>(
    bool IsSuccess,
    T? Value,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null)
{
    public static ServiceResult<T> Success(T value)
    {
        return new ServiceResult<T>(true, value, ErrorCode.None, string.Empty, string.Empty);
    }

    public static ServiceResult<T> Failure(
        ErrorCode errorCode,
        string userMessage,
        string technicalMessage,
        Exception? exception = null)
    {
        return new ServiceResult<T>(false, default, errorCode, userMessage, technicalMessage, exception);
    }
}
```

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~ServiceResultTests|FullyQualifiedName~PlaybackSnapshotTests"
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Enums VideoPlayer/Models VideoPlayer.Tests/Unit/Models
git commit -m "feat: add core playback models and service results"
```

---

### Task 3: Implement and Test the Playback State Machine

**Files:**
- Create: `VideoPlayer/Services/PlaybackStateMachine.cs`
- Create: `VideoPlayer.Tests/Unit/Services/PlaybackStateMachineTests.cs`

**Interfaces:**
- Consumes: `PlaybackState`.
- Produces:
  - `PlaybackStateMachine.Current -> PlaybackState`
  - `PlaybackStateMachine.TryTransition(PlaybackState next, out string? rejectionReason) -> bool`

- [ ] **Step 1: Write failing transition tests**

Create `VideoPlayer.Tests/Unit/Services/PlaybackStateMachineTests.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaybackStateMachineTests
{
    [Theory]
    [InlineData(PlaybackState.None, PlaybackState.Opening)]
    [InlineData(PlaybackState.Opening, PlaybackState.Playing)]
    [InlineData(PlaybackState.Playing, PlaybackState.Paused)]
    [InlineData(PlaybackState.Paused, PlaybackState.Playing)]
    [InlineData(PlaybackState.Playing, PlaybackState.Ended)]
    [InlineData(PlaybackState.Error, PlaybackState.Opening)]
    public void TryTransition_AllowsDocumentedTransitions(
        PlaybackState current,
        PlaybackState next)
    {
        var machine = new PlaybackStateMachine(current);

        var accepted = machine.TryTransition(next, out var reason);

        Assert.True(accepted);
        Assert.Null(reason);
        Assert.Equal(next, machine.Current);
    }

    [Fact]
    public void TryTransition_RejectsEndedToPlaying()
    {
        var machine = new PlaybackStateMachine(PlaybackState.Ended);

        var accepted = machine.TryTransition(PlaybackState.Playing, out var reason);

        Assert.False(accepted);
        Assert.Equal(PlaybackState.Ended, machine.Current);
        Assert.Contains("Ended", reason);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaybackStateMachineTests
```

Expected: FAIL because `PlaybackStateMachine` does not exist.

- [ ] **Step 3: Implement the state machine**

Create `VideoPlayer/Services/PlaybackStateMachine.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Services;

public sealed class PlaybackStateMachine
{
    private static readonly IReadOnlyDictionary<PlaybackState, PlaybackState[]> Allowed =
        new Dictionary<PlaybackState, PlaybackState[]>
        {
            [PlaybackState.None] = [PlaybackState.Opening],
            [PlaybackState.Opening] = [PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Error],
            [PlaybackState.Playing] = [PlaybackState.Paused, PlaybackState.Stopped, PlaybackState.Ended, PlaybackState.Error],
            [PlaybackState.Paused] = [PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Error],
            [PlaybackState.Stopped] = [PlaybackState.Opening],
            [PlaybackState.Ended] = [PlaybackState.Opening],
            [PlaybackState.Error] = [PlaybackState.Opening]
        };

    public PlaybackStateMachine(PlaybackState initial = PlaybackState.None)
    {
        Current = initial;
    }

    public PlaybackState Current { get; private set; }

    public bool TryTransition(PlaybackState next, out string? rejectionReason)
    {
        if (Allowed.TryGetValue(Current, out var targets) && targets.Contains(next))
        {
            Current = next;
            rejectionReason = null;
            return true;
        }

        rejectionReason = $"Transition {Current} -> {next} is not allowed.";
        return false;
    }
}
```

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaybackStateMachineTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Services/PlaybackStateMachine.cs VideoPlayer.Tests/Unit/Services/PlaybackStateMachineTests.cs
git commit -m "feat: add playback state machine"
```

---

### Task 4: Implement File Discovery and Natural Sorting

**Files:**
- Modify: `VideoPlayer/Helpers/PathHelper.cs`
- Create: `VideoPlayer/Interfaces/IFileService.cs`
- Create: `VideoPlayer/Services/FileService.cs`
- Modify: `VideoPlayer.Tests/Unit/Helpers/PathHelperTests.cs`
- Create: `VideoPlayer.Tests/Unit/Services/FileServiceTests.cs`

**Interfaces:**
- Consumes:
  - `PathHelper.NormalizePath(string) -> string`
  - `PathHelper.CreatePathKey(string) -> string`
  - `ServiceResult<T>`.
- Produces:
  - `PathHelper.NaturalCompare(string, string) -> int`
  - `IFileService.IsSupportedVideo(string) -> bool`
  - `IFileService.NormalizePath(string) -> string`
  - `IFileService.EnumerateVideosAsync(string, bool, CancellationToken) -> Task<ServiceResult<IReadOnlyList<string>>>`.

- [ ] **Step 1: Write failing natural-sort and file-discovery tests**

Append to `VideoPlayer.Tests/Unit/Helpers/PathHelperTests.cs`:

```csharp
[Fact]
public void NaturalCompare_OrdersMovie2BeforeMovie10()
{
    Assert.True(PathHelper.NaturalCompare("Movie2.mkv", "Movie10.mkv") < 0);
}
```

Create `VideoPlayer.Tests/Unit/Services/FileServiceTests.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class FileServiceTests
{
    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.MKV", true)]
    [InlineData("movie.txt", false)]
    public void IsSupportedVideo_RecognizesOnlyConfiguredExtensions(string path, bool expected)
    {
        var service = new FileService();

        Assert.Equal(expected, service.IsSupportedVideo(path));
    }

    [Fact]
    public async Task EnumerateVideosAsync_NaturallySortsAndDeduplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Movie10.mp4"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie2.mp4"), string.Empty);
            File.WriteAllText(Path.Combine(root, "notes.txt"), string.Empty);
            var service = new FileService();

            var result = await service.EnumerateVideosAsync(root, false, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value!.Count);
            Assert.EndsWith("Movie2.mp4", result.Value[0]);
            Assert.EndsWith("Movie10.mp4", result.Value[1]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EnumerateVideosAsync_ReturnsFileNotFoundForMissingFolder()
    {
        var service = new FileService();

        var result = await service.EnumerateVideosAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            false,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~PathHelperTests|FullyQualifiedName~FileServiceTests"
```

Expected: FAIL because natural comparison and `FileService` do not exist.

- [ ] **Step 3: Implement natural comparison and FileService**

Modify `VideoPlayer/Helpers/PathHelper.cs`:

```csharp
using System.Runtime.InteropServices;

using System.IO;

namespace VideoPlayer.Helpers;

public static class PathHelper
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public static string CreatePathKey(string path)
    {
        return NormalizePath(path).ToUpperInvariant();
    }

    public static int NaturalCompare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return StrCmpLogicalW(x, y);
    }
}
```

Create `VideoPlayer/Interfaces/IFileService.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IFileService
{
    bool IsSupportedVideo(string filePath);

    string NormalizePath(string filePath);

    Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default);
}
```

Create `VideoPlayer/Services/FileService.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class FileService : IFileService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".webm"
        };

    public bool IsSupportedVideo(string filePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    public string NormalizePath(string filePath)
    {
        return PathHelper.NormalizePath(filePath);
    }

    public Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    return ServiceResult<IReadOnlyList<string>>.Failure(
                        ErrorCode.FileNotFound,
                        "文件夹不存在。",
                        folderPath);
                }

                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                IReadOnlyList<string> paths = Directory
                    .EnumerateFiles(folderPath, "*", option)
                    .Where(IsSupportedVideo)
                    .Select(PathHelper.NormalizePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, Comparer<string>.Create(PathHelper.NaturalCompare))
                    .ToArray();

                return ServiceResult<IReadOnlyList<string>>.Success(paths);
            }
            catch (UnauthorizedAccessException exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.NoPermission,
                    "没有读取该文件夹的权限。",
                    folderPath,
                    exception);
            }
            catch (Exception exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.Unknown,
                    "扫描文件夹失败。",
                    folderPath,
                    exception);
            }
        }, cancellationToken);
    }
}
```
- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~PathHelperTests|FullyQualifiedName~FileServiceTests"
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Helpers/PathHelper.cs VideoPlayer/Interfaces/IFileService.cs VideoPlayer/Services/FileService.cs VideoPlayer.Tests/Unit/Helpers/PathHelperTests.cs VideoPlayer.Tests/Unit/Services/FileServiceTests.cs
git commit -m "feat: add file discovery and natural sorting"
```

---

### Task 5: Add Application Paths and Validated Settings Models

**Files:**
- Create: `VideoPlayer/Configuration/AppPaths.cs`
- Create: `VideoPlayer/Models/AppSettings.cs`
- Create: `VideoPlayer/Services/SettingsValidator.cs`
- Create: `VideoPlayer.Tests/Unit/Configuration/AppPathsTests.cs`
- Create: `VideoPlayer.Tests/Unit/Services/SettingsValidatorTests.cs`

**Interfaces:**
- Consumes: `PathHelper`.
- Produces:
  - `AppPaths(string rootDirectory)`.
  - `AppPaths.DatabasePath`, `ConfigPath`, `ConfigBackupPath`, `LogDirectory`, `ScreenshotDirectory`.
  - `AppSettings.CreateDefault() -> AppSettings`.
  - `SettingsValidator.Normalize(AppSettings) -> AppSettings`.

- [ ] **Step 1: Write failing path and settings tests**

Create `VideoPlayer.Tests/Unit/Configuration/AppPathsTests.cs`:

```csharp
using VideoPlayer.Configuration;
using Xunit;

namespace VideoPlayer.Tests.Unit.Configuration;

public sealed class AppPathsTests
{
    [Fact]
    public void Constructor_BuildsExpectedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoPlayerTests");

        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(root, "VideoPlayer.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(root, "appsettings.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(root, "Logs"), paths.LogDirectory);
    }

    [Fact]
    public void EnsureCoreDirectories_CreatesRootAndLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        try
        {
            paths.EnsureCoreDirectories();

            Assert.True(Directory.Exists(root));
            Assert.True(Directory.Exists(paths.LogDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
```

Create `VideoPlayer.Tests/Unit/Services/SettingsValidatorTests.cs`:

```csharp
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Normalize_ClampsInvalidValues()
    {
        var settings = AppSettings.CreateDefault();
        settings.Player.DefaultVolume = 500;
        settings.Player.DefaultPlaybackRate = 9.0;
        settings.Subtitle.Encoding = "GBK";

        var actual = SettingsValidator.Normalize(settings);

        Assert.Equal(100, actual.Player.DefaultVolume);
        Assert.Equal(2.0, actual.Player.DefaultPlaybackRate);
        Assert.Equal("Auto", actual.Subtitle.Encoding);
    }

    [Fact]
    public void Normalize_RepairsNullNestedSections()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 0,
            Player = null!,
            Subtitle = null!,
            UI = null!,
            Screenshot = null!
        };

        var actual = SettingsValidator.Normalize(settings);

        Assert.Equal(1, actual.SchemaVersion);
        Assert.NotNull(actual.Player);
        Assert.NotNull(actual.Subtitle);
        Assert.NotNull(actual.UI);
        Assert.NotNull(actual.Screenshot);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~SettingsValidatorTests"
```

Expected: FAIL because `AppPaths`, `AppSettings`, and `SettingsValidator` do not exist.

- [ ] **Step 3: Implement paths, settings, and validator**

Create `VideoPlayer/Configuration/AppPaths.cs`:

```csharp
namespace VideoPlayer.Configuration;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string DatabasePath => Path.Combine(RootDirectory, "VideoPlayer.db");

    public string ConfigPath => Path.Combine(RootDirectory, "appsettings.json");

    public string ConfigBackupPath => Path.Combine(RootDirectory, "appsettings.json.bak");

    public string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public string ScreenshotDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "VideoPlayer");

    public static AppPaths CreateDefault()
    {
        return new AppPaths(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VideoPlayer"));
    }

    public void EnsureCoreDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
```

Create `VideoPlayer/Models/AppSettings.cs`:

```csharp
namespace VideoPlayer.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public PlayerSettings Player { get; set; } = new();

    public SubtitleSettings Subtitle { get; set; } = new();

    public UiSettings UI { get; set; } = new();

    public ScreenshotSettings Screenshot { get; set; } = new();

    public static AppSettings CreateDefault() => new();
}

public sealed class PlayerSettings
{
    public int DefaultVolume { get; set; } = 80;

    public double DefaultPlaybackRate { get; set; } = 1.0;

    public bool AutoPlayNext { get; set; } = true;

    public bool RememberPosition { get; set; } = true;
}

public sealed class SubtitleSettings
{
    public bool AutoLoad { get; set; } = true;

    public string Encoding { get; set; } = "Auto";
}

public sealed class UiSettings
{
    public string Theme { get; set; } = "Dark";

    public bool ShowPlaylist { get; set; }
}

public sealed class ScreenshotSettings
{
    public string Directory { get; set; } = string.Empty;
}
```

Create `VideoPlayer/Services/SettingsValidator.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public static class SettingsValidator
{
    private static readonly HashSet<string> AllowedEncodings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Auto", "UTF-8", "GB18030", "UTF-16"
        };

    public static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= AppSettings.CreateDefault();
        settings.SchemaVersion = 1;
        settings.Player ??= new PlayerSettings();
        settings.Subtitle ??= new SubtitleSettings();
        settings.UI ??= new UiSettings();
        settings.Screenshot ??= new ScreenshotSettings();

        settings.Player.DefaultVolume = Math.Clamp(settings.Player.DefaultVolume, 0, 100);
        settings.Player.DefaultPlaybackRate = Math.Clamp(settings.Player.DefaultPlaybackRate, 0.5, 2.0);
        settings.Subtitle.Encoding = AllowedEncodings.Contains(settings.Subtitle.Encoding)
            ? settings.Subtitle.Encoding
            : "Auto";
        settings.Screenshot.Directory = string.IsNullOrWhiteSpace(settings.Screenshot.Directory)
            ? string.Empty
            : Path.GetFullPath(settings.Screenshot.Directory);

        return settings;
    }
}
```

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~AppPathsTests|FullyQualifiedName~SettingsValidatorTests"
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Configuration/AppPaths.cs VideoPlayer/Models/AppSettings.cs VideoPlayer/Services/SettingsValidator.cs VideoPlayer.Tests/Unit/Configuration/AppPathsTests.cs VideoPlayer.Tests/Unit/Services/SettingsValidatorTests.cs
git commit -m "feat: add application paths and settings validation"
```

---

### Task 6: Persist Settings with Backup and Corrupt-File Recovery

**Files:**
- Create: `VideoPlayer/Interfaces/ISettingsService.cs`
- Create: `VideoPlayer/Services/SettingsService.cs`
- Create: `VideoPlayer.Tests/Integration/Settings/SettingsServiceTests.cs`

**Interfaces:**
- Consumes:
  - `AppPaths`, `AppSettings`, `SettingsValidator`, `ServiceResult`.
- Produces:
  - `ISettingsService.Current -> AppSettings`
  - `ISettingsService.LoadAsync(CancellationToken) -> Task<ServiceResult>`
  - `ISettingsService.SaveAsync(CancellationToken) -> Task<ServiceResult>`
  - `ISettingsService.SaveImmediateAsync(CancellationToken) -> Task<ServiceResult>`

- [ ] **Step 1: Write failing persistence tests**

Create `VideoPlayer.Tests/Integration/Settings/SettingsServiceTests.cs`:

```csharp
using System.Text.Json;
using VideoPlayer.Configuration;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Integration.Settings;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SaveImmediateAsync_CreatesBackupWhenMainExists()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            var service = new SettingsService(paths);
            await service.SaveImmediateAsync();
            service.Current.Player.DefaultVolume = 55;

            var result = await service.SaveImmediateAsync();

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(paths.ConfigPath));
            Assert.True(File.Exists(paths.ConfigBackupPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesValidBackupWhenMainIsCorrupt()
    {
        var root = CreateRoot();
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var backup = AppSettings.CreateDefault();
            backup.Player.DefaultVolume = 23;
            await File.WriteAllTextAsync(
                paths.ConfigBackupPath,
                JsonSerializer.Serialize(backup));
            var service = new SettingsService(paths);

            var result = await service.LoadAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(23, service.Current.Player.DefaultVolume);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~SettingsServiceTests
```

Expected: FAIL because `SettingsService` does not exist.

- [ ] **Step 3: Implement SettingsService**

Create `VideoPlayer/Interfaces/ISettingsService.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveImmediateAsync(CancellationToken cancellationToken = default);
}
```

Create `VideoPlayer/Services/SettingsService.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~SettingsServiceTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Interfaces/ISettingsService.cs VideoPlayer/Services/SettingsService.cs VideoPlayer.Tests/Integration/Settings
git commit -m "feat: persist settings with recovery"
```

---

### Task 7: Add DI, UI Dispatching, Logging, and Application Shell

**Files:**
- Create: `VideoPlayer/Interfaces/IUiDispatcher.cs`
- Create: `VideoPlayer/Services/WpfUiDispatcher.cs`
- Create: `VideoPlayer/CompositionRoot.cs`
- Modify: `VideoPlayer/App.xaml`
- Modify: `VideoPlayer/App.xaml.cs`
- Modify: `VideoPlayer/MainWindow.xaml`
- Modify: `VideoPlayer/MainWindow.xaml.cs`
- Create: `VideoPlayer.Tests/Unit/Composition/CompositionRootTests.cs`

**Interfaces:**
- Consumes: `IFileService`, `ISettingsService`, `AppPaths`.
- Produces: `IUiDispatcher`; `CompositionRoot.Create(AppPaths, ILogger) -> ServiceProvider`.

- [ ] **Step 1: Install production dependencies**

Run:

```powershell
dotnet add VideoPlayer/VideoPlayer.csproj package LibVLCSharp.WPF --version 3.10.1
dotnet add VideoPlayer/VideoPlayer.csproj package VideoLAN.LibVLC.Windows --version 3.0.23.1
dotnet add VideoPlayer/VideoPlayer.csproj package CommunityToolkit.Mvvm --version 8.4.2
dotnet add VideoPlayer/VideoPlayer.csproj package Microsoft.Extensions.DependencyInjection --version 10.0.12
dotnet add VideoPlayer/VideoPlayer.csproj package Serilog --version 4.4.0
dotnet add VideoPlayer/VideoPlayer.csproj package Serilog.Sinks.File --version 7.0.0
```

- [ ] **Step 2: Write the failing composition test**

Create `VideoPlayer.Tests/Unit/Composition/CompositionRootTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using Xunit;

namespace VideoPlayer.Tests.Unit.Composition;

public sealed class CompositionRootTests
{
    [Fact]
    public void Create_ResolvesFileAndSettingsServices()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            using var provider = CompositionRoot.Create(
                paths,
                new LoggerConfiguration().CreateLogger());

            Assert.NotNull(provider.GetRequiredService<IFileService>());
            Assert.Same(
                provider.GetRequiredService<ISettingsService>(),
                provider.GetRequiredService<ISettingsService>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
```

- [ ] **Step 3: Run test and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~CompositionRootTests
```

Expected: FAIL because `CompositionRoot` does not exist.

- [ ] **Step 4: Implement dispatcher and composition root**

Create `VideoPlayer/Interfaces/IUiDispatcher.cs`:

```csharp
namespace VideoPlayer.Interfaces;

public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> action);
}
```

Create `VideoPlayer/Services/WpfUiDispatcher.cs`:

```csharp
using System.Windows.Threading;
using VideoPlayer.Interfaces;

namespace VideoPlayer.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public Task<T> InvokeAsync<T>(Func<T> action) => _dispatcher.InvokeAsync(action).Task;
}
```

Create `VideoPlayer/CompositionRoot.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;
using VideoPlayer.Services;

namespace VideoPlayer;

public static class CompositionRoot
{
    public static ServiceProvider Create(AppPaths paths, ILogger logger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(logger);
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
```

- [ ] **Step 5: Replace the WPF application shell**

Replace `VideoPlayer/App.xaml`:

```xml
<Application x:Class="VideoPlayer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Replace `VideoPlayer/App.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using VideoPlayer.Configuration;
using VideoPlayer.Interfaces;

namespace VideoPlayer;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.CreateDefault();
        paths.EnsureCoreDirectories();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "yyyy-MM-dd.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();

        try
        {
            _services = CompositionRoot.Create(paths, Log.Logger);
            var settings = _services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();

            var window = new MainWindow(settings.Current);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application startup failed.");
            MessageBox.Show("VideoPlayer 启动失败。", "VideoPlayer");
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

Replace `VideoPlayer/MainWindow.xaml`:

```xml
<Window x:Class="VideoPlayer.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="VideoPlayer"
        Width="1100"
        Height="700"
        MinWidth="800"
        MinHeight="500"
        Background="#111111">
    <Grid>
        <TextBlock Text="VideoPlayer"
                   Foreground="White"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center" />
    </Grid>
</Window>
```

Replace `VideoPlayer/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using VideoPlayer.Models;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }
}
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~CompositionRootTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

```powershell
git add VideoPlayer/App.xaml VideoPlayer/App.xaml.cs VideoPlayer/MainWindow.xaml VideoPlayer/MainWindow.xaml.cs VideoPlayer/CompositionRoot.cs VideoPlayer/Interfaces/IUiDispatcher.cs VideoPlayer/Services/WpfUiDispatcher.cs VideoPlayer.Tests/Unit/Composition/CompositionRootTests.cs
git commit -m "feat: add DI and application shell"
```

---

### Task 8: Implement PlayerService Lifecycle and Minimal Playback Commands

**Files:**
- Create: `VideoPlayer/Models/Events/PlaybackStateChangedEventArgs.cs`
- Create: `VideoPlayer/Models/Events/MediaOpenedEventArgs.cs`
- Create: `VideoPlayer/Models/Events/PlaybackFailedEventArgs.cs`
- Create: `VideoPlayer/Interfaces/IPlayerService.cs`
- Create: `VideoPlayer/Services/PlayerService.cs`
- Modify: `VideoPlayer/CompositionRoot.cs`
- Create: `VideoPlayer.Tests/Integration/Player/PlayerServiceTests.cs`

**Interfaces:**
- Consumes: `LibVLC`, `MediaPlayer`, `PlaybackStateMachine`, `ServiceResult`, `IUiDispatcher`.
- Produces: `IPlayerService` with `MediaPlayer`, `Snapshot`, `OpenAsync`, `PlayAsync`, `PauseAsync`, `StopAsync`, `StateChanged`, `MediaOpened`, `PlaybackFailed`.

- [ ] **Step 1: Write the failing lifecycle tests**

Create `VideoPlayer.Tests/Integration/Player/PlayerServiceTests.cs`:

```csharp
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.Player;

public sealed class PlayerServiceTests
{
    [Fact]
    public async Task Constructor_InitializesLibVlcAndDisposesIdempotently()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        var service = new PlayerService(
            new ImmediateUiDispatcher(),
            logger,
            ["--vout=dummy"]);

        Assert.True(service.IsInitialized);
        await service.DisposeAsync();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_MissingFileReturnsFileNotFound()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        await using var service = new PlayerService(
            new ImmediateUiDispatcher(),
            logger,
            ["--vout=dummy"]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");

        var result = await service.OpenAsync(
            new OpenMediaRequest(path, TimeSpan.Zero, true, null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerServiceTests
```

Expected: FAIL because `PlayerService` and `IPlayerService` do not exist.

- [ ] **Step 3: Add event argument types**

Create `VideoPlayer/Models/Events/PlaybackStateChangedEventArgs.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Models.Events;

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public PlaybackState OldState { get; init; }
    public PlaybackState NewState { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}
```

Create `VideoPlayer/Models/Events/MediaOpenedEventArgs.cs`:

```csharp
namespace VideoPlayer.Models.Events;

public sealed class MediaOpenedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}
```

Create `VideoPlayer/Models/Events/PlaybackFailedEventArgs.cs`:

```csharp
using VideoPlayer.Enums;

namespace VideoPlayer.Models.Events;

public sealed class PlaybackFailedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public ErrorCode ErrorCode { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string TechnicalMessage { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
```

- [ ] **Step 4: Define the initial player contract**

Create `VideoPlayer/Interfaces/IPlayerService.cs`:

```csharp
using LibVLCSharp.Shared;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;

namespace VideoPlayer.Interfaces;

public interface IPlayerService : IAsyncDisposable
{
    MediaPlayer? MediaPlayer { get; }
    bool IsInitialized { get; }
    bool HasMedia { get; }
    PlaybackSnapshot Snapshot { get; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default);
}
```
- [ ] **Step 5: Implement PlayerService lifecycle and commands**

Create `VideoPlayer/Services/PlayerService.cs`:

```csharp
using LibVLCSharp.Shared;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;

namespace VideoPlayer.Services;

public sealed class PlayerService : IPlayerService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly PlaybackStateMachine _stateMachine = new();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;
    private long _sessionId;
    private bool _disposed;

    public PlayerService(
        IUiDispatcher dispatcher,
        ILogger logger,
        string[]? vlcOptions = null)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        Core.Initialize();
        _libVlc = new LibVLC(vlcOptions ?? []);
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.Opening += OnOpening;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.Paused += OnPaused;
        _mediaPlayer.Stopped += OnStopped;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
    }

    public MediaPlayer? MediaPlayer => _mediaPlayer;
    public bool IsInitialized => !_disposed;
    public bool HasMedia => _mediaPlayer.Media is not null;

    public PlaybackSnapshot Snapshot => new(
        Interlocked.Read(ref _sessionId),
        _stateMachine.Current,
        _mediaPlayer.Media?.Mrl,
        GetPosition(),
        GetDuration(),
        _mediaPlayer.Volume,
        _mediaPlayer.Mute,
        _mediaPlayer.Rate,
        _mediaPlayer.IsSeekable,
        _mediaPlayer.VideoTrackCount > 0,
        _mediaPlayer.AudioTrackCount > 0,
        DateTimeOffset.UtcNow);

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public async Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            string path;
            try
            {
                path = PathHelper.NormalizePath(request.FilePath);
            }
            catch (Exception exception)
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.InvalidPath,
                    "视频路径无效。",
                    request.FilePath,
                    exception);
            }

            if (!File.Exists(path))
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.FileNotFound,
                    "视频文件不存在。",
                    path);
            }

            _media?.Dispose();
            _media = new Media(_libVlc, new Uri(path));
            _mediaPlayer.Media = _media;
            var sessionId = Interlocked.Increment(ref _sessionId);
            Transition(PlaybackState.Opening, sessionId);

            _dispatcher.Post(() => MediaOpened?.Invoke(
                this,
                new MediaOpenedEventArgs
                {
                    MediaSessionId = sessionId,
                    FilePath = path,
                    OccurredAtUtc = DateTimeOffset.UtcNow
                }));

            if (request.AutoPlay)
            {
                _mediaPlayer.Play();
            }

            return ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    sessionId,
                    path,
                    request.AutoPlay,
                    request.StartPosition,
                    DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            var sessionId = Interlocked.Read(ref _sessionId);
            _logger.Error(exception, "Failed to open media {Path}", request.FilePath);
            RaiseFailure(sessionId, ErrorCode.Unknown, "无法打开视频。", exception);
            return ServiceResult<MediaSessionInfo>.Failure(
                ErrorCode.Unknown,
                "无法打开视频。",
                request.FilePath,
                exception);
        }
        finally
        {
            _commandGate.Release();
        }
    }
    public async Task<ServiceResult> PlayAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!HasMedia)
            {
                return ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "没有可播放的视频。",
                    "No media");
            }

            return _mediaPlayer.Play()
                ? ServiceResult.Success()
                : ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "无法开始播放。",
                    "MediaPlayer.Play returned false.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (_stateMachine.Current != PlaybackState.Playing)
            {
                return ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "当前没有正在播放的视频。",
                    "Pause rejected.");
            }

            _mediaPlayer.Pause();
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return ServiceResult.Success();
            }

            _mediaPlayer.Stop();
            Transition(PlaybackState.Stopped, Interlocked.Read(ref _sessionId));
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _commandGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mediaPlayer.Opening -= OnOpening;
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.EncounteredError -= OnEncounteredError;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _media?.Dispose();
            _libVlc.Dispose();
        }
        finally
        {
            _commandGate.Release();
            _commandGate.Dispose();
        }
    }

    private void OnOpening(object? sender, EventArgs e)
        => Transition(PlaybackState.Opening, Interlocked.Read(ref _sessionId));

    private void OnPlaying(object? sender, EventArgs e)
        => Transition(PlaybackState.Playing, Interlocked.Read(ref _sessionId));

    private void OnPaused(object? sender, EventArgs e)
        => Transition(PlaybackState.Paused, Interlocked.Read(ref _sessionId));

    private void OnStopped(object? sender, EventArgs e)
        => Transition(PlaybackState.Stopped, Interlocked.Read(ref _sessionId));

    private void OnEndReached(object? sender, EventArgs e)
        => Transition(PlaybackState.Ended, Interlocked.Read(ref _sessionId));

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        var sessionId = Interlocked.Read(ref _sessionId);
        Transition(PlaybackState.Error, sessionId);
        RaiseFailure(sessionId, ErrorCode.UnsupportedMedia, "视频无法播放。", null);
    }

    private void Transition(PlaybackState next, long sessionId)
    {
        var oldState = _stateMachine.Current;
        if (!_stateMachine.TryTransition(next, out var reason))
        {
            _logger.Debug("Ignored state transition: {Reason}", reason);
            return;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs
            {
                MediaSessionId = sessionId,
                OldState = oldState,
                NewState = next,
                OccurredAtUtc = DateTimeOffset.UtcNow
            }));
    }

    private void RaiseFailure(
        long sessionId,
        ErrorCode code,
        string message,
        Exception? exception)
    {
        _dispatcher.Post(() => PlaybackFailed?.Invoke(
            this,
            new PlaybackFailedEventArgs
            {
                MediaSessionId = sessionId,
                ErrorCode = code,
                UserMessage = message,
                TechnicalMessage = exception?.Message ?? message,
                Exception = exception
            }));
    }

    private TimeSpan GetPosition()
    {
        var value = _mediaPlayer.Time;
        return value < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(value);
    }

    private TimeSpan GetDuration()
    {
        var value = _mediaPlayer.Length;
        return value <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(value);
    }
}
```

- [ ] **Step 6: Register PlayerService as a singleton**

Modify `VideoPlayer/CompositionRoot.cs` before `return services.BuildServiceProvider(...)`:

```csharp
services.AddSingleton<IUiDispatcher>(_ =>
    new WpfUiDispatcher(System.Windows.Application.Current.Dispatcher));
services.AddSingleton<IPlayerService>(provider =>
    new PlayerService(
        provider.GetRequiredService<IUiDispatcher>(),
        provider.GetRequiredService<ILogger>()));
```

- [ ] **Step 7: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerServiceTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 8: Commit**

```powershell
git add VideoPlayer/Models/Events VideoPlayer/Interfaces/IPlayerService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/CompositionRoot.cs VideoPlayer.Tests/Integration/Player
git commit -m "feat: add LibVLC player service lifecycle"
```

---

### Task 9: Wire PlayerViewModel and VideoView for Minimal Playback

**Files:**
- Create: `VideoPlayer/Views/PlayerView.xaml`
- Create: `VideoPlayer/Views/PlayerView.xaml.cs`
- Create: `VideoPlayer/ViewModels/PlayerViewModel.cs`
- Create: `VideoPlayer/ViewModels/MainViewModel.cs`
- Modify: `VideoPlayer/MainWindow.xaml`
- Modify: `VideoPlayer/MainWindow.xaml.cs`
- Modify: `VideoPlayer/CompositionRoot.cs`
- Create: `VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelTests.cs`

**Interfaces:**
- Consumes: `IPlayerService`.
- Produces: `PlayerViewModel.OpenAsync`, `PlayAsync`, `PauseAsync`, `StopAsync`, `MediaPlayer`, and `MainViewModel.Player`.

- [ ] **Step 1: Write the failing view-model tests**

Create `VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelTests.cs`:

```csharp
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelTests
{
    [Fact]
    public async Task OpenAsync_ForwardsRequestAndRequestsAutoPlay()
    {
        var fake = new FakePlayerService();
        var viewModel = new PlayerViewModel(fake);

        await viewModel.OpenAsync(@"C:\视频\movie.mp4");

        Assert.Equal(@"C:\视频\movie.mp4", fake.LastRequest!.FilePath);
        Assert.True(fake.LastRequest.AutoPlay);
    }

    [Fact]
    public async Task PlayAsync_WhenServiceFails_SetsLastError()
    {
        var fake = new FakePlayerService
        {
            PlayResult = ServiceResult.Failure(
                Enums.ErrorCode.InvalidState,
                "没有视频。",
                "No media")
        };
        var viewModel = new PlayerViewModel(fake);

        await viewModel.PlayAsync();

        Assert.Equal("没有视频。", viewModel.LastError);
    }

    private sealed class FakePlayerService : IPlayerService
    {
        public OpenMediaRequest? LastRequest { get; private set; }
        public ServiceResult PlayResult { get; set; } = ServiceResult.Success();
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
        public bool IsInitialized => true;
        public bool HasMedia => true;
        public PlaybackSnapshot Snapshot => new(
            1, Enums.PlaybackState.Playing, "movie.mp4",
            TimeSpan.Zero, TimeSpan.FromMinutes(1), 80, false, 1.0,
            true, true, true, DateTimeOffset.UtcNow);
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
        public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

        public Task<ServiceResult<MediaSessionInfo>> OpenAsync(
            OpenMediaRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(1, request.FilePath, true, TimeSpan.Zero, DateTimeOffset.UtcNow)));
        }

        public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PlayResult);

        public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelTests
```

Expected: FAIL because the ViewModels do not exist.
- [ ] **Step 3: Implement the ViewModels**

Create `VideoPlayer/ViewModels/PlayerViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerService _playerService;

    public PlayerViewModel(IPlayerService playerService)
    {
        _playerService = playerService;
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [RelayCommand]
    public async Task OpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var request = new OpenMediaRequest(
            filePath,
            TimeSpan.Zero,
            AutoPlay: true,
            ExternalSubtitlePath: null,
            PreferredAudioLanguage: null);
        var result = await _playerService.OpenAsync(request, cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.PlayAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.PauseAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.StopAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Create `VideoPlayer/ViewModels/MainViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(PlayerViewModel player)
    {
        Player = player;
    }

    public PlayerViewModel Player { get; }
}
```

- [ ] **Step 4: Create PlayerView and bind VideoView**

Create `VideoPlayer/Views/PlayerView.xaml`:

```xml
<UserControl x:Class="VideoPlayer.Views.PlayerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <vlc:VideoView x:Name="VideoView"
                       MediaPlayer="{Binding MediaPlayer}"
                       Background="Black" />
        <Border Grid.Row="1" Background="#1B1B1B" Padding="8">
            <StackPanel Orientation="Horizontal">
                <Button Content="打开" Click="Open_Click" Margin="4" />
                <Button Content="播放" Command="{Binding PlayCommand}" Margin="4" />
                <Button Content="暂停" Command="{Binding PauseCommand}" Margin="4" />
                <Button Content="停止" Command="{Binding StopCommand}" Margin="4" />
                <TextBlock Text="{Binding LastError}"
                           Foreground="#FF6B6B"
                           VerticalAlignment="Center"
                           Margin="12,0" />
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

Create `VideoPlayer/Views/PlayerView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Views;

public partial class PlayerView : UserControl, IDisposable
{
    public PlayerView()
    {
        InitializeComponent();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.ts;*.m2ts;*.webm|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true && DataContext is PlayerViewModel viewModel)
        {
            await viewModel.OpenAsync(dialog.FileName);
        }
    }

    public void Dispose()
    {
        VideoView.Dispose();
    }
}
```
- [ ] **Step 5: Connect MainWindow and DI**

Replace `VideoPlayer/MainWindow.xaml`:

```xml
<Window x:Class="VideoPlayer.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:VideoPlayer.Views"
        Title="VideoPlayer"
        Width="1100"
        Height="700"
        MinWidth="800"
        MinHeight="500"
        Background="#111111">
    <views:PlayerView x:Name="PlayerView" DataContext="{Binding Player}" />
</Window>
```

Replace `VideoPlayer/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using VideoPlayer.Views;
using VideoPlayer.ViewModels;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        PlayerView.Dispose();
        base.OnClosed(e);
    }
}
```

Modify `VideoPlayer/CompositionRoot.cs` before `return`:

```csharp
services.AddSingleton<PlayerViewModel>();
services.AddSingleton<MainViewModel>();
```

Modify `VideoPlayer/App.xaml.cs` window creation:

```csharp
var viewModel = _services.GetRequiredService<MainViewModel>();
var window = new MainWindow(viewModel);
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

```powershell
git add VideoPlayer/Views VideoPlayer/ViewModels VideoPlayer/MainWindow.xaml VideoPlayer/MainWindow.xaml.cs VideoPlayer/CompositionRoot.cs VideoPlayer/App.xaml.cs VideoPlayer.Tests/Unit/ViewModels
git commit -m "feat: add minimal player view and view model"
```


---

### Task 10: Add Seek, Volume, Mute, Rate, and Periodic Progress Refresh

**Files:**
- Create: `VideoPlayer/Helpers/PlaybackMath.cs`
- Modify: `VideoPlayer/Interfaces/IPlayerService.cs`
- Modify: `VideoPlayer/Services/PlayerService.cs`
- Modify: `VideoPlayer/ViewModels/PlayerViewModel.cs`
- Modify: `VideoPlayer/Views/PlayerView.xaml`
- Create: `VideoPlayer.Tests/Unit/Helpers/PlaybackMathTests.cs`

**Interfaces:**
- Consumes: `IPlayerService`, `PlaybackSnapshot`.
- Produces: `PlaybackMath.ClampPosition`, `ClampVolume`, `ClampRate`; seek/volume/mute/rate player methods; ViewModel progress properties.

- [ ] **Step 1: Write failing boundary tests**

Create `VideoPlayer.Tests/Unit/Helpers/PlaybackMathTests.cs`:

```csharp
using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class PlaybackMathTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 5)]
    [InlineData(20, 10)]
    public void ClampPosition_UsesInclusiveRange(int input, int expected)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expected),
            PlaybackMath.ClampPosition(TimeSpan.FromSeconds(input), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ClampVolume_UsesZeroToOneHundred()
        => Assert.Equal(100, PlaybackMath.ClampVolume(500));

    [Fact]
    public void ClampRate_UsesHalfToDouble()
        => Assert.Equal(2.0, PlaybackMath.ClampRate(9.0));
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaybackMathTests
```

Expected: FAIL because `PlaybackMath` does not exist.

- [ ] **Step 3: Implement boundary helpers**

Create `VideoPlayer/Helpers/PlaybackMath.cs`:

```csharp
using System.IO;

namespace VideoPlayer.Helpers;

public static class PlaybackMath
{
    public static TimeSpan ClampPosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > duration ? duration : position;
    }

    public static int ClampVolume(int volume) => Math.Clamp(volume, 0, 100);

    public static double ClampRate(double rate) => Math.Clamp(rate, 0.5, 2.0);
}
```

- [ ] **Step 4: Extend the player contract and implementation**

Add to `VideoPlayer/Interfaces/IPlayerService.cs`:

```csharp
Task<ServiceResult> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
Task<ServiceResult> SetVolumeAsync(int volume, CancellationToken cancellationToken = default);
Task<ServiceResult> SetMuteAsync(bool isMuted, CancellationToken cancellationToken = default);
Task<ServiceResult> SetRateAsync(double rate, CancellationToken cancellationToken = default);
```

Add to `VideoPlayer/Services/PlayerService.cs` before `DisposeAsync`:

```csharp
public async Task<ServiceResult> SeekAsync(
    TimeSpan position,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        if (!HasMedia)
        {
            return ServiceResult.Failure(ErrorCode.InvalidState, "没有可播放的视频。", "No media");
        }

        if (!_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0)
        {
            return ServiceResult.Failure(ErrorCode.SeekNotAvailable, "当前视频不支持跳转。", "Not seekable");
        }

        var target = PlaybackMath.ClampPosition(
            position,
            TimeSpan.FromMilliseconds(_mediaPlayer.Length));
        _mediaPlayer.Time = (long)target.TotalMilliseconds;
        return ServiceResult.Success();
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> SetVolumeAsync(
    int volume,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        _mediaPlayer.Volume = PlaybackMath.ClampVolume(volume);
        return ServiceResult.Success();
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> SetMuteAsync(
    bool isMuted,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        _mediaPlayer.Mute = isMuted;
        return ServiceResult.Success();
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> SetRateAsync(
    double rate,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        _mediaPlayer.SetRate((float)PlaybackMath.ClampRate(rate));
        return ServiceResult.Success();
    }
    finally
    {
        _commandGate.Release();
    }
}
```

- [ ] **Step 5: Add progress refresh and control bindings**

Add these members to `VideoPlayer/ViewModels/PlayerViewModel.cs`:

```csharp
private CancellationTokenSource? _progressCts;

[ObservableProperty] private TimeSpan _position;
[ObservableProperty] private TimeSpan _duration;
[ObservableProperty] private int _volume = 80;
[ObservableProperty] private double _rate = 1.0;
[ObservableProperty] private bool _isMuted;

[RelayCommand]
public async Task SeekAsync(CancellationToken cancellationToken = default)
{
    var result = await _playerService.SeekAsync(Position, cancellationToken);
    LastError = result.IsSuccess ? string.Empty : result.UserMessage;
}

[RelayCommand]
public async Task ToggleMuteAsync(CancellationToken cancellationToken = default)
{
    IsMuted = !IsMuted;
    var result = await _playerService.SetMuteAsync(IsMuted, cancellationToken);
    LastError = result.IsSuccess ? string.Empty : result.UserMessage;
}

public void StartProgressRefresh()
{
    _progressCts?.Cancel();
    _progressCts = new CancellationTokenSource();
    _ = RunProgressLoopAsync(_progressCts.Token);
}

private async Task RunProgressLoopAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
    try
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var snapshot = _playerService.Snapshot;
            Position = snapshot.Position;
            Duration = snapshot.Duration;
            Volume = snapshot.Volume;
            Rate = snapshot.Rate;
            IsMuted = snapshot.IsMuted;
        }
    }
    catch (OperationCanceledException)
    {
    }
}

public async ValueTask DisposeAsync()
{
    _progressCts?.Cancel();
    _progressCts?.Dispose();
    await ValueTask.CompletedTask;
}
```

Call `StartProgressRefresh()` in the successful branch of `PlayerViewModel.OpenAsync`.

Add these partial callbacks so direct volume and rate changes reach the service:

```csharp
partial void OnVolumeChanged(int value)
{
    _ = _playerService.SetVolumeAsync(value);
}

partial void OnRateChanged(double value)
{
    _ = _playerService.SetRateAsync(value);
}
```

Add to `VideoPlayer/Views/PlayerView.xaml`:

```xml
<Slider Width="220" Minimum="0"
        Maximum="{Binding Duration.TotalSeconds}"
        Value="{Binding Position.TotalSeconds}" />
<TextBlock Foreground="White">
    <Run Text="{Binding Position}" /><Run Text=" / " /><Run Text="{Binding Duration}" />
</TextBlock>
<Slider Width="120" Minimum="0" Maximum="100" Value="{Binding Volume}" />
<Button Content="静音" Command="{Binding ToggleMuteCommand}" />
<TextBlock Text="{Binding Rate, StringFormat={}{0:0.00}x}" Foreground="White" />
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaybackMathTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

```powershell
git add VideoPlayer/Helpers/PlaybackMath.cs VideoPlayer/Interfaces/IPlayerService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/ViewModels/PlayerViewModel.cs VideoPlayer/Views/PlayerView.xaml VideoPlayer.Tests/Unit/Helpers/PlaybackMathTests.cs
git commit -m "feat: add seek volume rate and progress refresh"
```


---

### Task 11: Implement the Playlist Service

**Files:**
- Create: `VideoPlayer/Models/PlaylistItem.cs`
- Create: `VideoPlayer/Interfaces/IPlaylistService.cs`
- Create: `VideoPlayer/Services/PlaylistService.cs`
- Create: `VideoPlayer/Properties/AssemblyInfo.cs`
- Create: `VideoPlayer.Tests/Unit/Services/PlaylistServiceTests.cs`

**Interfaces:**
- Consumes: `IFileService`, `PathHelper`, `ServiceResult`.
- Produces: `Items`, `CurrentIndex`, `Current`, `AddFilesAsync`, `AddFolderAsync`, `RemoveAsync`, `ClearAsync`, `GetNext`, `GetPrevious`, `SetCurrent`.

- [ ] **Step 1: Write failing playlist tests**

Create `VideoPlayer.Tests/Unit/Services/PlaylistServiceTests.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class PlaylistServiceTests
{
    [Fact]
    public async Task AddFilesAsync_DeduplicatesAndPreservesOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "Movie1.mp4");
            var second = Path.Combine(root, "Movie2.mkv");
            File.WriteAllText(first, string.Empty);
            File.WriteAllText(second, string.Empty);
            var service = new PlaylistService(new FileService());

            await service.AddFilesAsync([first, first.ToUpperInvariant(), second], false);

            Assert.Equal(2, service.Items.Count);
            Assert.Equal(0, service.CurrentIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GetNext_AtLastItem_ReturnsInvalidStateWithoutWrapping()
    {
        var service = new PlaylistService(new FileService());
        service.SetCurrentForTest(["a.mp4", "b.mp4"], 1);

        var result = service.GetNext();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistServiceTests
```

Expected: FAIL because `PlaylistService` does not exist.

- [ ] **Step 3: Implement the playlist contract and service**

Create `VideoPlayer/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("VideoPlayer.Tests")]
```

Create `VideoPlayer/Models/PlaylistItem.cs`:

```csharp
namespace VideoPlayer.Models;

public sealed class PlaylistItem
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public TimeSpan? Duration { get; set; }
}
```

Create `VideoPlayer/Interfaces/IPlaylistService.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IPlaylistService
{
    IReadOnlyList<PlaylistItem> Items { get; }
    int CurrentIndex { get; }
    PlaylistItem? Current { get; }
    event EventHandler? PlaylistChanged;
    event EventHandler? CurrentChanged;

    Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default);
    ServiceResult<PlaylistItem> GetNext(bool wrap = false);
    ServiceResult<PlaylistItem> GetPrevious(bool wrap = false);
    ServiceResult SetCurrent(string filePath);
}
```

Create `VideoPlayer/Services/PlaylistService.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class PlaylistService : IPlaylistService
{
    private readonly IFileService _fileService;
    private readonly List<PlaylistItem> _items = [];

    public PlaylistService(IFileService fileService)
    {
        _fileService = fileService;
    }

    public IReadOnlyList<PlaylistItem> Items => _items;
    public int CurrentIndex { get; private set; } = -1;
    public PlaylistItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count
        ? _items[CurrentIndex]
        : null;

    public event EventHandler? PlaylistChanged;
    public event EventHandler? CurrentChanged;

    public async Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        var added = new List<PlaylistItem>();
        foreach (var rawPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(rawPath))
            {
                var folderResult = await _fileService.EnumerateVideosAsync(
                    rawPath, recursive, cancellationToken);
                if (!folderResult.IsSuccess)
                {
                    continue;
                }

                AddPaths(folderResult.Value!, added);
            }
            else if (File.Exists(rawPath) && _fileService.IsSupportedVideo(rawPath))
            {
                AddPaths([rawPath], added);
            }
        }

        if (added.Count == 0)
        {
            return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                ErrorCode.FileNotFound, "没有找到可加入的视频。", "No supported files.");
        }

        _items.AddRange(added);
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }

        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return ServiceResult<IReadOnlyList<PlaylistItem>>.Success(added);
    }

    public Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        return AddFolderCoreAsync(folderPath, recursive, cancellationToken);
    }

    private async Task<ServiceResult> AddFolderCoreAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var result = await AddFilesAsync([folderPath], recursive, cancellationToken);
        return result.IsSuccess
            ? ServiceResult.Success()
            : ServiceResult.Failure(result.ErrorCode, result.UserMessage, result.TechnicalMessage, result.Exception);
    }

    public Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var key = PathHelper.CreatePathKey(filePath);
        var index = _items.FindIndex(item => PathHelper.CreatePathKey(item.FilePath) == key);
        if (index < 0)
        {
            return Task.FromResult(ServiceResult.Failure(
                ErrorCode.FileNotFound, "播放列表中没有该视频。", filePath));
        }

        _items.RemoveAt(index);
        CurrentIndex = _items.Count == 0 ? -1 : Math.Min(CurrentIndex, _items.Count - 1);
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        _items.Clear();
        CurrentIndex = -1;
        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(ServiceResult.Success());
    }

    public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
    {
        if (_items.Count == 0)
        {
            return ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState, "播放列表为空。", "Empty playlist");
        }

        var target = wrap
            ? (CurrentIndex + 1) % _items.Count
            : CurrentIndex + 1;
        return target < _items.Count
            ? SetCurrentIndex(target)
            : ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState, "已经是最后一项。", "No next item");
    }

    public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
    {
        if (_items.Count == 0)
        {
            return ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState, "播放列表为空。", "Empty playlist");
        }

        var target = wrap
            ? (CurrentIndex - 1 + _items.Count) % _items.Count
            : CurrentIndex - 1;
        return target >= 0
            ? SetCurrentIndex(target)
            : ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState, "已经是第一项。", "No previous item");
    }

    public ServiceResult SetCurrent(string filePath)
    {
        var key = PathHelper.CreatePathKey(filePath);
        var index = _items.FindIndex(item => PathHelper.CreatePathKey(item.FilePath) == key);
        if (index < 0)
        {
            return ServiceResult.Failure(ErrorCode.InvalidState, "视频不在播放列表中。", filePath);
        }

        CurrentIndex = index;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return ServiceResult.Success();
    }

    internal void SetCurrentForTest(IEnumerable<string> paths, int index)
    {
        _items.Clear();
        _items.AddRange(paths.Select(path => new PlaylistItem { FilePath = path }));
        CurrentIndex = index;
    }

    private void AddPaths(IEnumerable<string> paths, List<PlaylistItem> added)
    {
        foreach (var path in paths)
        {
            var normalized = PathHelper.NormalizePath(path);
            var key = PathHelper.CreatePathKey(normalized);
            if (_items.Any(item => PathHelper.CreatePathKey(item.FilePath) == key) ||
                added.Any(item => PathHelper.CreatePathKey(item.FilePath) == key))
            {
                continue;
            }

            added.Add(new PlaylistItem { FilePath = normalized });
        }
    }

    private ServiceResult<PlaylistItem> SetCurrentIndex(int index)
    {
        CurrentIndex = index;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return ServiceResult<PlaylistItem>.Success(_items[index]);
    }
}
```

Expose the test helper through `InternalsVisibleTo`; do not make `SetCurrentForTest` public.

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistServiceTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Models/PlaylistItem.cs VideoPlayer/Interfaces/IPlaylistService.cs VideoPlayer/Services/PlaylistService.cs VideoPlayer.Tests/Unit/Services/PlaylistServiceTests.cs
git commit -m "feat: add playlist service"
```

---

### Task 12: Add the Virtualized Playlist View and Drag/Drop

**Files:**
- Create: `VideoPlayer/ViewModels/PlaylistViewModel.cs`
- Create: `VideoPlayer/Views/PlaylistView.xaml`
- Create: `VideoPlayer/Views/PlaylistView.xaml.cs`
- Modify: `VideoPlayer/ViewModels/MainViewModel.cs`
- Modify: `VideoPlayer/MainWindow.xaml`
- Modify: `VideoPlayer/CompositionRoot.cs`
- Create: `VideoPlayer.Tests/Unit/ViewModels/PlaylistViewModelTests.cs`

**Interfaces:**
- Consumes: `IPlaylistService`, `IFileService`.
- Produces: `PlaylistViewModel.Items`, `AddPathsAsync`, `RemoveAsync`, `ClearAsync`.

- [ ] **Step 1: Write failing ViewModel tests**

Create `VideoPlayer.Tests/Unit/ViewModels/PlaylistViewModelTests.cs`:

```csharp
using VideoPlayer.Services;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlaylistViewModelTests
{
    [Fact]
    public async Task AddPathsAsync_UpdatesObservableItems()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "movie.mp4");
            File.WriteAllText(file, string.Empty);
            var service = new PlaylistService(new FileService());
            var viewModel = new PlaylistViewModel(service);

            await viewModel.AddPathsAsync([file]);

            Assert.Single(viewModel.Items);
            Assert.Equal(file, viewModel.Items[0].FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistViewModelTests
```

Expected: FAIL because `PlaylistViewModel` does not exist.

- [ ] **Step 3: Implement PlaylistViewModel**

Create `VideoPlayer/ViewModels/PlaylistViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;

    public PlaylistViewModel(IPlaylistService playlistService)
    {
        _playlistService = playlistService;
        _playlistService.PlaylistChanged += (_, _) => Refresh();
    }

    public ObservableCollection<PlaylistItem> Items { get; } = [];

    [ObservableProperty]
    private string _lastError = string.Empty;

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var result = await _playlistService.AddFilesAsync(paths, recursive: false);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        Refresh();
    }

    public async Task RemoveAsync(PlaylistItem item)
    {
        var result = await _playlistService.RemoveAsync(item.FilePath);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        Refresh();
    }

    public async Task ClearAsync()
    {
        var result = await _playlistService.ClearAsync();
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        foreach (var item in _playlistService.Items)
        {
            Items.Add(item);
        }
    }
}
```

- [ ] **Step 4: Create a virtualized playlist view**

Create `VideoPlayer/Views/PlaylistView.xaml`:

```xml
<UserControl x:Class="VideoPlayer.Views.PlaylistView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AllowDrop="True"
             Drop="OnDrop"
             DragOver="OnDragOver">
    <Grid Background="#151515">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="播放列表" Foreground="White" Margin="10" FontSize="16" />
        <ListBox Grid.Row="1"
                 ItemsSource="{Binding Items}"
                 Background="Transparent"
                 Foreground="White"
                 BorderThickness="0"
                 VirtualizingPanel.IsVirtualizing="True"
                 VirtualizingPanel.VirtualizationMode="Recycling">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding FileName}" Padding="8,4" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <Button Grid.Row="2" Content="清空" Command="{Binding ClearCommand}" Margin="10" />
    </Grid>
</UserControl>
```

Create `VideoPlayer/Views/PlaylistView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            DataContext is PlaylistViewModel viewModel)
        {
            await viewModel.AddPathsAsync(paths);
        }
    }
}
```

- [ ] **Step 5: Integrate the playlist into MainWindow and DI**

Modify `MainViewModel` to accept and expose `PlaylistViewModel`.

Replace the root of `MainWindow.xaml` with:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="300" />
    </Grid.ColumnDefinitions>
    <views:PlayerView x:Name="PlayerView" DataContext="{Binding Player}" />
    <views:PlaylistView Grid.Column="1" DataContext="{Binding Playlist}" />
</Grid>
```

Modify `CompositionRoot` registrations:

```csharp
services.AddSingleton<IPlaylistService, PlaylistService>();
services.AddSingleton<PlaylistViewModel>();
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistViewModelTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

```powershell
git add VideoPlayer/ViewModels/PlaylistViewModel.cs VideoPlayer/Views/PlaylistView.xaml VideoPlayer/Views/PlaylistView.xaml.cs VideoPlayer/ViewModels/MainViewModel.cs VideoPlayer/MainWindow.xaml VideoPlayer/CompositionRoot.cs VideoPlayer.Tests/Unit/ViewModels/PlaylistViewModelTests.cs
git commit -m "feat: add virtualized playlist and drag drop"
```


---

### Task 13: Add Auto-Play Next and Navigation Race Protection

**Files:**
- Create: `VideoPlayer/Models/ResumeChoice.cs`
- Create: `VideoPlayer/Interfaces/IDialogService.cs`
- Create: `VideoPlayer/Models/Events/EndReachedEventArgs.cs`
- Modify: `VideoPlayer/Interfaces/IPlayerService.cs`
- Modify: `VideoPlayer/Services/PlayerService.cs`
- Modify: `VideoPlayer/ViewModels/PlayerViewModel.cs`
- Create: `VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelNavigationTests.cs`
- Create: `VideoPlayer.Tests/Fakes/PlayerViewModelFixture.cs`

**Interfaces:**
- Consumes: `IPlaylistService`, `ISettingsService`, `IDialogService`, `IPlayerService.EndReached`.
- Produces: `PlayerViewModel.NextAsync`, `PreviousAsync`, `HandleEndReachedAsync`, navigation session filtering.

- [ ] **Step 1: Write failing duplicate-end and stale-session tests**

Create `VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelNavigationTests.cs`:

```csharp
using VideoPlayer.Models.Events;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelNavigationTests
{
    [Fact]
    public async Task HandleEndReachedAsync_ForSameSession_OpensNextOnlyOnce()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 4);
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");
        var args = new EndReachedEventArgs
        {
            MediaSessionId = 4,
            FilePath = @"C:\Videos\a.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        await fixture.ViewModel.HandleEndReachedAsync(args);
        await fixture.ViewModel.HandleEndReachedAsync(args);

        Assert.Equal(1, fixture.Player.OpenCount);
    }

    [Fact]
    public async Task HandleEndReachedAsync_ForOldSession_DoesNothing()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 9);
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");

        await fixture.ViewModel.HandleEndReachedAsync(new EndReachedEventArgs
        {
            MediaSessionId = 8,
            FilePath = @"C:\Videos\old.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal(1, fixture.Player.OpenCount);
    }
}
```

Create VideoPlayer.Tests/Fakes/PlayerViewModelFixture.cs:

~~~csharp
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Tests.Fakes;

public sealed class PlayerViewModelFixture
{
    public PlayerViewModelFixture(long sessionId)
    {
        Player = new FakePlayerService(sessionId);
        ViewModel = new PlayerViewModel(
            Player,
            new FakePlaylistService(),
            new FakeSettingsService(),
            new FakeDialogService());
    }

    public FakePlayerService Player { get; }
    public PlayerViewModel ViewModel { get; }
}

internal sealed class FakePlayerService : IPlayerService
{
    private readonly long _sessionId;

    public FakePlayerService(long sessionId) => _sessionId = sessionId;

    public int OpenCount { get; private set; }
    public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
    public bool IsInitialized => true;
    public bool HasMedia => true;
    public PlaybackSnapshot Snapshot => new(
        _sessionId, Enums.PlaybackState.Playing, "a.mp4",
        TimeSpan.Zero, TimeSpan.FromMinutes(1), 80, false, 1.0,
        true, true, true, DateTimeOffset.UtcNow);
    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    public event EventHandler<EndReachedEventArgs>? EndReached;

    public Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenCount++;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            MediaSessionId = _sessionId,
            OldState = Enums.PlaybackState.None,
            NewState = Enums.PlaybackState.Playing,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
            new MediaSessionInfo(_sessionId, request.FilePath, true, TimeSpan.Zero, DateTimeOffset.UtcNow)));
    }

    public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetVolumeAsync(int volume, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetMuteAsync(bool isMuted, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetRateAsync(double rate, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> TakeSnapshotAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(outputPath, "png");
        return Task.FromResult(ServiceResult.Success());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakePlaylistService : IPlaylistService
{
    private readonly PlaylistItem[] _items =
    [
        new() { FilePath = "a.mp4" },
        new() { FilePath = "b.mp4" }
    ];
    private int _index;

    public IReadOnlyList<PlaylistItem> Items => _items;
    public int CurrentIndex => _index;
    public PlaylistItem? Current => _items[_index];
    public event EventHandler? PlaylistChanged;
    public event EventHandler? CurrentChanged;

    public Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(IEnumerable<string> paths, bool recursive, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult<IReadOnlyList<PlaylistItem>>.Success(_items));
    public Task<ServiceResult> AddFolderAsync(string folderPath, bool recursive, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> RemoveAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
        => _index + 1 < _items.Length
            ? ServiceResult<PlaylistItem>.Success(_items[++_index])
            : ServiceResult<PlaylistItem>.Failure(Enums.ErrorCode.InvalidState, "Last", "Last");
    public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
        => _index > 0
            ? ServiceResult<PlaylistItem>.Success(_items[--_index])
            : ServiceResult<PlaylistItem>.Failure(Enums.ErrorCode.InvalidState, "First", "First");
    public ServiceResult SetCurrent(string filePath) => ServiceResult.Success();
}

internal sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; } = AppSettings.CreateDefault();
    public Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SaveImmediateAsync(CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult.Success());
}

internal sealed class FakeDialogService : IDialogService
{
    public Task<ResumeChoice> AskResumeAsync(string fileName, TimeSpan lastPosition, CancellationToken cancellationToken = default)
        => Task.FromResult(ResumeChoice.Continue);
}
~~~

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelNavigationTests
```

Expected: FAIL because the navigation behavior is not implemented.

- [ ] **Step 3: Add end-reached contract and navigation implementation**

Create `VideoPlayer/Models/Events/EndReachedEventArgs.cs`:

```csharp
namespace VideoPlayer.Models.Events;

public sealed class EndReachedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}
```

Add to `IPlayerService`:

```csharp
event EventHandler<EndReachedEventArgs>? EndReached;
```

In `PlayerService`, add the event and raise it from the LibVLC callback:

```csharp
public event EventHandler<EndReachedEventArgs>? EndReached;

private void OnEndReached(object? sender, EventArgs e)
{
    var sessionId = Interlocked.Read(ref _sessionId);
    Transition(PlaybackState.Ended, sessionId);
    _dispatcher.Post(() => EndReached?.Invoke(this, new EndReachedEventArgs
    {
        MediaSessionId = sessionId,
        FilePath = _mediaPlayer.Media?.Mrl ?? string.Empty,
        OccurredAtUtc = DateTimeOffset.UtcNow
    }));
}
```

Replace the existing `OnEndReached` expression-bodied method with this implementation.

Add to `PlayerViewModel`:

```csharp
private readonly IPlaylistService _playlistService;
private readonly ISettingsService _settingsService;
private readonly IDialogService _dialogService;
private long _activeSessionId;
private long _handledEndSessionId;
private int _navigationVersion;

public PlayerViewModel(
    IPlayerService playerService,
    IPlaylistService playlistService,
    ISettingsService settingsService,
    IDialogService dialogService)
{
    _playerService = playerService;
    _playlistService = playlistService;
    _settingsService = settingsService;
    _dialogService = dialogService;
    _playerService.StateChanged += OnStateChanged;
    _playerService.EndReached += OnEndReached;
}

private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
{
    if (e.MediaSessionId >= _activeSessionId)
    {
        _activeSessionId = e.MediaSessionId;
    }
}

private async void OnEndReached(object? sender, EndReachedEventArgs e)
{
    await HandleEndReachedAsync(e);
}

public async Task HandleEndReachedAsync(EndReachedEventArgs args)
{
    if (args.MediaSessionId != _activeSessionId ||
        Interlocked.Exchange(ref _handledEndSessionId, args.MediaSessionId) == args.MediaSessionId)
    {
        return;
    }

    if (!_settingsService.Current.Player.AutoPlayNext)
    {
        return;
    }

    var next = _playlistService.GetNext();
    if (next.IsSuccess)
    {
        await OpenAsync(next.Value!.FilePath);
    }
}

public async Task NextAsync()
{
    Interlocked.Increment(ref _navigationVersion);
    var next = _playlistService.GetNext();
    if (next.IsSuccess)
    {
        await OpenAsync(next.Value!.FilePath);
    }
    else
    {
        LastError = next.UserMessage;
    }
}

public async Task PreviousAsync()
{
    Interlocked.Increment(ref _navigationVersion);
    var previous = _playlistService.GetPrevious();
    if (previous.IsSuccess)
    {
        await OpenAsync(previous.Value!.FilePath);
    }
    else
    {
        LastError = previous.UserMessage;
    }
}
```

Define the dialog contract in `VideoPlayer/Interfaces/IDialogService.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IDialogService
{
    Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default);
}
```

Create `VideoPlayer/Models/ResumeChoice.cs`:

```csharp
namespace VideoPlayer.Models;

public enum ResumeChoice
{
    Continue,
    Restart,
    Cancel
}
```

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelNavigationTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 5: Commit**

```powershell
git add VideoPlayer/Models/ResumeChoice.cs VideoPlayer/Interfaces/IDialogService.cs VideoPlayer/Models/Events/EndReachedEventArgs.cs VideoPlayer/Interfaces/IPlayerService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/ViewModels/PlayerViewModel.cs VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelNavigationTests.cs
git commit -m "feat: add auto next and stale session protection"
```

---

### Task 14: Add Subtitle Discovery, Track Switching, and Track Events

**Files:**
- Create: `VideoPlayer/Models/Events/MediaParsedEventArgs.cs`
- Create: `VideoPlayer/Models/Events/TracksChangedEventArgs.cs`
- Create: `VideoPlayer/Interfaces/ISubtitleService.cs`
- Create: `VideoPlayer/Services/SubtitleService.cs`
- Modify: `VideoPlayer/Interfaces/IPlayerService.cs`
- Modify: `VideoPlayer/Services/PlayerService.cs`
- Modify: `VideoPlayer/ViewModels/PlayerViewModel.cs`
- Create: `VideoPlayer.Tests/Unit/Services/SubtitleServiceTests.cs`

**Interfaces:**
- Consumes: `PlayerTrackInfo`, `MediaPlayer`, `ServiceResult`.
- Produces: `ISubtitleService.FindAutoSubtitleAsync`; track list and selection methods on `IPlayerService`.

- [ ] **Step 1: Write failing subtitle tests**

Create `VideoPlayer.Tests/Unit/Services/SubtitleServiceTests.cs`:

```csharp
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class SubtitleServiceTests
{
    [Fact]
    public async Task FindAutoSubtitleAsync_PrefersSrt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var media = Path.Combine(root, "Movie.mkv");
            File.WriteAllText(media, string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.ass"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.srt"), string.Empty);
            var service = new SubtitleService();

            var result = await service.FindAutoSubtitleAsync(media);

            Assert.True(result.IsSuccess);
            Assert.EndsWith("Movie.srt", result.Value);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FindAutoSubtitleAsync_ReturnsFailureWhenAbsent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        var service = new SubtitleService();

        var result = await service.FindAutoSubtitleAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(Enums.ErrorCode.SubtitleNotFound, result.ErrorCode);
    }
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~SubtitleServiceTests
```

Expected: FAIL because `SubtitleService` does not exist.

- [ ] **Step 3: Implement SubtitleService**

Create `VideoPlayer/Interfaces/ISubtitleService.cs`:

```csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface ISubtitleService
{
    Task<ServiceResult<string>> FindAutoSubtitleAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ValidateSubtitleAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default);
}
```

Create `VideoPlayer/Services/SubtitleService.cs`:

```csharp
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class SubtitleService : ISubtitleService
{
    private static readonly string[] Extensions = [".srt", ".ass", ".ssa"];

    public Task<ServiceResult<string>> FindAutoSubtitleAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        var media = PathHelper.NormalizePath(mediaPath);
        var basePath = Path.Combine(
            Path.GetDirectoryName(media)!,
            Path.GetFileNameWithoutExtension(media));
        foreach (var extension in Extensions)
        {
            var candidate = basePath + extension;
            if (File.Exists(candidate))
            {
                return Task.FromResult(ServiceResult<string>.Success(candidate));
            }
        }

        return Task.FromResult(ServiceResult<string>.Failure(
            ErrorCode.SubtitleNotFound, "没有找到自动字幕。", basePath));
    }

    public Task<ServiceResult> ValidateSubtitleAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default)
    {
        var valid = File.Exists(subtitlePath) &&
            Extensions.Contains(Path.GetExtension(subtitlePath), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(valid
            ? ServiceResult.Success()
            : ServiceResult.Failure(ErrorCode.SubtitleNotFound, "字幕文件无效。", subtitlePath));
    }
}
```

- [ ] **Step 4: Extend player tracks and events**

Add event argument classes:

```csharp
// VideoPlayer/Models/Events/MediaParsedEventArgs.cs
public sealed class MediaParsedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}

// VideoPlayer/Models/Events/TracksChangedEventArgs.cs
public sealed class TracksChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}
```

Add to `IPlayerService`:

```csharp
event EventHandler<MediaParsedEventArgs>? MediaParsed;
event EventHandler<TracksChangedEventArgs>? TracksChanged;

Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
    PlayerTrackType type,
    CancellationToken cancellationToken = default);

Task<ServiceResult> SelectAudioTrackAsync(
    int trackId,
    CancellationToken cancellationToken = default);

Task<ServiceResult> SelectSubtitleTrackAsync(
    int? trackId,
    CancellationToken cancellationToken = default);

Task<ServiceResult> AddExternalSubtitleAsync(
    string filePath,
    bool select,
    CancellationToken cancellationToken = default);
```

Implement in `PlayerService` using the current LibVLCSharp API:

```csharp
public async Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
    PlayerTrackType type,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        var tracks = type == PlayerTrackType.Audio
            ? _mediaPlayer.AudioTrackDescription
                .Select(track => new PlayerTrackInfo(
                    PlayerTrackType.Audio, track.Id, track.Name ?? $"音轨 {track.Id}",
                    track.Name ?? "und", track.Id == _mediaPlayer.AudioTrack, false))
                .ToArray()
            : _mediaPlayer.SpuDescription
                .Select(track => new PlayerTrackInfo(
                    PlayerTrackType.Subtitle, track.Id, track.Name ?? $"字幕 {track.Id}",
                    track.Name ?? "und", track.Id == _mediaPlayer.Spu, false))
                .ToArray();

        return ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success(tracks);
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> SelectAudioTrackAsync(
    int trackId,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        return _mediaPlayer.SetAudioTrack(trackId)
            ? ServiceResult.Success()
            : ServiceResult.Failure(ErrorCode.TrackNotFound, "音轨不可用。", $"Track {trackId}");
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> SelectSubtitleTrackAsync(
    int? trackId,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        return _mediaPlayer.SetSpu(trackId ?? -1)
            ? ServiceResult.Success()
            : ServiceResult.Failure(ErrorCode.TrackNotFound, "字幕轨不可用。", $"Track {trackId}");
    }
    finally
    {
        _commandGate.Release();
    }
}

public async Task<ServiceResult> AddExternalSubtitleAsync(
    string filePath,
    bool select,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        return _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(filePath).AbsoluteUri, select)
            ? ServiceResult.Success()
            : ServiceResult.Failure(ErrorCode.SubtitleNotFound, "字幕加载失败。", filePath);
    }
    finally
    {
        _commandGate.Release();
    }
}
```

Use `MediaPlayer.LengthChanged` and `_media.ParsedChanged` to publish the new event objects on the UI dispatcher. Do not block in the callbacks.

- [ ] **Step 5: Connect automatic subtitles to open flow**

In `PlayerViewModel.OpenAsync`, call `ISubtitleService.FindAutoSubtitleAsync` before constructing `OpenMediaRequest`. If a subtitle is found, assign it to `ExternalSubtitlePath`; otherwise leave it null.

Add audio/subtitle collections and commands to `PlayerViewModel`:

```csharp
public ObservableCollection<PlayerTrackInfo> AudioTracks { get; } = [];
public ObservableCollection<PlayerTrackInfo> SubtitleTracks { get; } = [];

[RelayCommand]
public async Task SelectAudioTrackAsync(PlayerTrackInfo track)
{
    var result = await _playerService.SelectAudioTrackAsync(track.Id);
    LastError = result.IsSuccess ? string.Empty : result.UserMessage;
}

[RelayCommand]
public async Task SelectSubtitleTrackAsync(PlayerTrackInfo? track)
{
    var result = await _playerService.SelectSubtitleTrackAsync(track?.Id);
    LastError = result.IsSuccess ? string.Empty : result.UserMessage;
}
```

- [ ] **Step 6: Run tests and build**

Run:

```powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~SubtitleServiceTests
dotnet build VideoPlayer.sln -c Release
```

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

```powershell
git add VideoPlayer/Models/Events VideoPlayer/Interfaces VideoPlayer/Services/SubtitleService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/ViewModels/PlayerViewModel.cs VideoPlayer.Tests/Unit/Services/SubtitleServiceTests.cs
git commit -m "feat: add subtitle and track switching"
```


---

### Task 15: Add the SQLite Model, Migration, and Recovery Initializer

**Files:**
- Create: VideoPlayer/Models/PlaybackHistory.cs
- Create: VideoPlayer/Database/VideoPlayerDbContext.cs
- Create: VideoPlayer/Database/VideoPlayerDbContextFactory.cs
- Create: VideoPlayer/Database/DatabaseInitializer.cs
- Create: VideoPlayer.Tests/Integration/Database/DatabaseInitializerTests.cs
- Generate: VideoPlayer/Database/Migrations

**Interfaces:**
- Consumes: AppPaths, PathHelper, IDbContextFactory<VideoPlayerDbContext>.
- Produces: VideoPlayerDbContext.PlaybackHistory; DatabaseInitializer.InitializeAsync; recoverable history availability.

- [ ] **Step 1: Install EF Core and the local migration tool**

~~~powershell
dotnet add VideoPlayer/VideoPlayer.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.12
dotnet add VideoPlayer/VideoPlayer.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.12
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.12
~~~

- [ ] **Step 2: Write failing database tests**

Create VideoPlayer.Tests/Integration/Database/DatabaseInitializerTests.cs:

~~~csharp
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Database;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Integration.Database;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndMigrates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var result = await new DatabaseInitializer(paths).InitializeAsync();

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(paths.DatabasePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UniquePathKey_PreventsDuplicateRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            await new DatabaseInitializer(paths).InitializeAsync();
            var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
                .UseSqlite("Data Source=" + paths.DatabasePath)
                .Options;
            await using var context = new VideoPlayerDbContext(options);
            context.PlaybackHistory.Add(new PlaybackHistory
            {
                FilePath = "C:\\Videos\\Movie.mp4",
                PathKey = "C:\\VIDEOS\\MOVIE.MP4",
                FileName = "Movie.mp4",
                LastPlayedAtUtc = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
            context.PlaybackHistory.Add(new PlaybackHistory
            {
                FilePath = "c:\\videos\\movie.mp4",
                PathKey = "C:\\VIDEOS\\MOVIE.MP4",
                FileName = "movie.mp4",
                LastPlayedAtUtc = DateTimeOffset.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
~~~

- [ ] **Step 3: Run tests and verify failure**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~DatabaseInitializerTests
~~~

Expected: FAIL because the database types do not exist.

- [ ] **Step 4: Implement the model and context**

Create VideoPlayer/Models/PlaybackHistory.cs:

~~~csharp
namespace VideoPlayer.Models;

public sealed class PlaybackHistory
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string PathKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long DurationTicks { get; set; }
    public long LastPositionTicks { get; set; }
    public DateTimeOffset LastPlayedAtUtc { get; set; }
    public int PlayCount { get; set; }
    public bool IsCompleted { get; set; }
}
~~~

Create VideoPlayer/Database/VideoPlayerDbContext.cs:

~~~csharp
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
~~~

Create VideoPlayer/Database/VideoPlayerDbContextFactory.cs:

~~~csharp
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
~~~

- [ ] **Step 5: Generate and verify the initial migration**

~~~powershell
dotnet ef migrations add InitialCreate --project VideoPlayer/VideoPlayer.csproj --startup-project VideoPlayer/VideoPlayer.csproj --output-dir Database/Migrations
~~~

Expected: migration files create PlaybackHistory and the unique PathKey index.

- [ ] **Step 6: Implement recovery initialization**

Create VideoPlayer/Database/DatabaseInitializer.cs:

~~~csharp
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Models;

namespace VideoPlayer.Database;

public sealed class DatabaseInitializer
{
    private readonly AppPaths _paths;

    public DatabaseInitializer(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ServiceResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCoreDirectories();
        try
        {
            await MigrateAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (Exception firstFailure)
        {
            try
            {
                await RecoverAsync(cancellationToken);
                await MigrateAsync(cancellationToken);
                return ServiceResult.Success();
            }
            catch (Exception recoveryFailure)
            {
                return ServiceResult.Failure(
                    ErrorCode.DatabaseUnavailable,
                    "播放历史暂时不可用。",
                    "Initial: " + firstFailure.Message + "; Recovery: " + recoveryFailure.Message,
                    recoveryFailure);
            }
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite("Data Source=" + _paths.DatabasePath)
            .Options;
        await using var context = new VideoPlayerDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (File.Exists(_paths.DatabasePath))
        {
            var backup = Path.Combine(
                _paths.RootDirectory,
                "VideoPlayer.corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".db");
            File.Move(_paths.DatabasePath, backup, overwrite: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
~~~

- [ ] **Step 7: Register the context factory and initializer**

Add to CompositionRoot:

~~~csharp
services.AddDbContextFactory<VideoPlayerDbContext>(options =>
    options.UseSqlite("Data Source=" + paths.DatabasePath));
services.AddSingleton<DatabaseInitializer>();
~~~

Call DatabaseInitializer.InitializeAsync during startup after settings loading and before MainWindow creation.

- [ ] **Step 8: Run tests and build**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~DatabaseInitializerTests
dotnet build VideoPlayer.sln -c Release
~~~

Expected: PASS and successful Release build.

- [ ] **Step 9: Commit**

~~~powershell
git add .config VideoPlayer/Models/PlaybackHistory.cs VideoPlayer/Database VideoPlayer/CompositionRoot.cs VideoPlayer/App.xaml.cs VideoPlayer.Tests/Integration/Database
git commit -m "feat: add SQLite model migration and recovery"
~~~


---

### Task 16: Implement Resume Policy and HistoryService

**Files:**
- Create: VideoPlayer/Models/ResumeInfo.cs
- Create: VideoPlayer/Services/ResumePolicy.cs
- Create: VideoPlayer/Interfaces/IHistoryService.cs
- Create: VideoPlayer/Services/HistoryService.cs
- Create: VideoPlayer.Tests/Unit/Services/ResumePolicyTests.cs
- Create: VideoPlayer.Tests/Integration/History/HistoryServiceTests.cs

**Interfaces:**
- Consumes: IDbContextFactory<VideoPlayerDbContext>, PathHelper, PlaybackSnapshot.
- Produces: ResumePolicy.ShouldAsk; IHistoryService resume/start/save/complete/flush methods.

- [ ] **Step 1: Write failing resume-policy tests**

Create VideoPlayer.Tests/Unit/Services/ResumePolicyTests.cs:

~~~csharp
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ResumePolicyTests
{
    [Fact]
    public void ShouldAsk_AtMidpoint_ReturnsTrue()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            false, DateTimeOffset.UtcNow, 1);

        Assert.True(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenCompleted_ReturnsFalse()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5),
            true, DateTimeOffset.UtcNow, 1);

        Assert.False(ResumePolicy.ShouldAsk(info));
    }

    [Fact]
    public void ShouldAsk_WhenOnlyFiveSecondsIn_ReturnsFalse()
    {
        var info = new ResumeInfo(
            "movie.mp4", TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5),
            false, DateTimeOffset.UtcNow, 1);

        Assert.False(ResumePolicy.ShouldAsk(info));
    }
}
~~~

- [ ] **Step 2: Run tests and verify failure**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~ResumePolicyTests
~~~

Expected: FAIL because ResumePolicy does not exist.

- [ ] **Step 3: Implement ResumeInfo and ResumePolicy**

Create VideoPlayer/Models/ResumeInfo.cs:

~~~csharp
namespace VideoPlayer.Models;

public sealed record ResumeInfo(
    string FilePath,
    TimeSpan Duration,
    TimeSpan LastPosition,
    bool IsCompleted,
    DateTimeOffset LastPlayedAtUtc,
    int PlayCount);
~~~

Create VideoPlayer/Services/ResumePolicy.cs:

~~~csharp
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public static class ResumePolicy
{
    public static bool ShouldAsk(ResumeInfo info)
    {
        if (info.IsCompleted || info.Duration < TimeSpan.FromSeconds(30))
        {
            return false;
        }

        if (info.LastPosition < TimeSpan.FromSeconds(15))
        {
            return false;
        }

        if (info.LastPosition >= info.Duration * 0.95)
        {
            return false;
        }

        return info.Duration - info.LastPosition >= TimeSpan.FromSeconds(10);
    }
}
~~~

- [ ] **Step 4: Define the HistoryService contract**

Create VideoPlayer/Interfaces/IHistoryService.cs:

~~~csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IHistoryService
{
    bool IsAvailable { get; }

    Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveProgressAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default);
}
~~~

- [ ] **Step 5: Implement serialized upsert history writes**

Create VideoPlayer/Services/HistoryService.cs:

~~~csharp
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Database;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class HistoryService : IHistoryService
{
    private readonly IDbContextFactory<VideoPlayerDbContext> _contextFactory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public HistoryService(IDbContextFactory<VideoPlayerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public bool IsAvailable { get; private set; } = true;

    public async Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return ServiceResult<ResumeInfo?>.Success(null);
        }

        try
        {
            var key = PathHelper.CreatePathKey(filePath);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.PlaybackHistory.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PathKey == key, cancellationToken);
            return ServiceResult<ResumeInfo?>.Success(row is null ? null : new ResumeInfo(
                row.FilePath,
                TimeSpan.FromTicks(row.DurationTicks),
                TimeSpan.FromTicks(row.LastPositionTicks),
                row.IsCompleted,
                row.LastPlayedAtUtc,
                row.PlayCount));
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史不可用。", filePath, exception);
        }
    }

    public Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.DurationTicks = duration.Ticks;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            row.PlayCount += 1;
            row.IsCompleted = false;
        }, cancellationToken);

    public Task<ServiceResult> SaveProgressAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.MediaPath is null)
        {
            return Task.FromResult(ServiceResult.Success());
        }

        return UpsertAsync(snapshot.MediaPath, row =>
        {
            row.DurationTicks = snapshot.Duration.Ticks;
            row.LastPositionTicks = snapshot.Position.Ticks;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);
    }

    public Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.DurationTicks = duration.Ticks;
            row.LastPositionTicks = 0;
            row.IsCompleted = true;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.LastPositionTicks = 0;
            row.IsCompleted = false;
        }, cancellationToken);

    public Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());

    private async Task<ServiceResult> UpsertAsync(
        string filePath,
        Action<PlaybackHistory> update,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ServiceResult.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史不可用。", filePath);
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var normalized = PathHelper.NormalizePath(filePath);
            var key = PathHelper.CreatePathKey(normalized);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.PlaybackHistory
                .SingleOrDefaultAsync(x => x.PathKey == key, cancellationToken);
            if (row is null)
            {
                row = new PlaybackHistory
                {
                    FilePath = normalized,
                    PathKey = key,
                    FileName = Path.GetFileName(normalized)
                };
                context.PlaybackHistory.Add(row);
            }

            update(row);
            await context.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            return ServiceResult.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史保存失败。", filePath, exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
~~~

- [ ] **Step 6: Add integration tests**

Create VideoPlayer.Tests/Integration/History/HistoryServiceTests.cs that initializes a temporary database and verifies:

1. Calling StartPlaybackAsync twice leaves one row with PlayCount equal to 2.
2. SaveProgressAsync stores the supplied position and duration.
3. MarkCompletedAsync sets IsCompleted and clears LastPositionTicks.
4. ClearCompletedAsync resets IsCompleted without deleting the row.
5. An invalid database path makes IsAvailable false without throwing.

- [ ] **Step 7: Register HistoryService and run tests**

Add to CompositionRoot:

~~~csharp
services.AddSingleton<IHistoryService, HistoryService>();
~~~

Run:

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~ResumePolicyTests|FullyQualifiedName~HistoryServiceTests"
dotnet build VideoPlayer.sln -c Release
~~~

Expected: PASS and successful Release build.

- [ ] **Step 8: Commit**

~~~powershell
git add VideoPlayer/Models/ResumeInfo.cs VideoPlayer/Services/ResumePolicy.cs VideoPlayer/Interfaces/IHistoryService.cs VideoPlayer/Services/HistoryService.cs VideoPlayer/CompositionRoot.cs VideoPlayer.Tests/Unit/Services/ResumePolicyTests.cs VideoPlayer.Tests/Integration/History
git commit -m "feat: add resume policy and playback history"
~~~


---

### Task 17: Wire Resume, Play Count, and Periodic Progress Persistence

**Files:**
- Create: VideoPlayer/Services/WpfDialogService.cs
- Modify: VideoPlayer/Interfaces/IDialogService.cs
- Modify: VideoPlayer/Services/PlayerService.cs
- Modify: VideoPlayer/ViewModels/PlayerViewModel.cs
- Modify: VideoPlayer/CompositionRoot.cs
- Create: VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelHistoryTests.cs

**Interfaces:**
- Consumes: IHistoryService, ISettingsService, IDialogService, ResumePolicy.
- Produces: resume prompt flow, first-Playing play count, five-second history flush loop.

- [ ] **Step 1: Write failing resume-flow tests**

Create VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelHistoryTests.cs with a FakeHistoryService and FakeDialogService. Assert:

1. A 50 percent ResumeInfo and ResumeChoice.Continue produces OpenMediaRequest.StartPosition equal to the saved position.
2. ResumeChoice.Restart produces TimeSpan.Zero and calls ClearCompletedAsync.
3. ResumeChoice.Cancel does not call IPlayerService.OpenAsync.
4. A second Playing state for the same MediaSessionId does not increment PlayCount twice.

- [ ] **Step 2: Run tests and verify failure**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelHistoryTests
~~~

Expected: FAIL because history orchestration is absent.

- [ ] **Step 3: Implement WpfDialogService**

Create VideoPlayer/Services/WpfDialogService.cs:

~~~csharp
using System.Windows;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default)
    {
        var message =
            "是否继续播放 " + fileName + "？\n\n上次播放位置：" +
            lastPosition.ToString(@"hh\:mm\:ss");
        var result = MessageBox.Show(
            message,
            "继续播放",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return Task.FromResult(result switch
        {
            MessageBoxResult.Yes => ResumeChoice.Continue,
            MessageBoxResult.No => ResumeChoice.Restart,
            _ => ResumeChoice.Cancel
        });
    }
}
~~~

Register it as a singleton IDialogService.

- [ ] **Step 4: Apply StartPosition once after playback begins**

Add to PlayerService:

~~~csharp
private TimeSpan? _pendingStartPosition;
private long _resumeAppliedSessionId = -1;
~~~

In OpenAsync, before assigning the new Media:

~~~csharp
_pendingStartPosition = request.StartPosition > TimeSpan.Zero
    ? request.StartPosition
    : null;
~~~

Replace the existing OnPlaying method with:

~~~csharp
private void OnPlaying(object? sender, EventArgs e)
{
    var sessionId = Interlocked.Read(ref _sessionId);
    if (_pendingStartPosition is { } start &&
        Interlocked.Exchange(ref _resumeAppliedSessionId, sessionId) != sessionId)
    {
        _mediaPlayer.Time = (long)start.TotalMilliseconds;
        _pendingStartPosition = null;
    }

    Transition(PlaybackState.Playing, sessionId);
}
~~~

- [ ] **Step 5: Add resume orchestration to PlayerViewModel**

Inject IHistoryService into PlayerViewModel and store it as _historyService. Replace the successful part of OpenAsync with this sequence:

~~~csharp
var resume = await _historyService.GetResumeInfoAsync(filePath, cancellationToken);
var startPosition = TimeSpan.Zero;
if (resume.IsSuccess && resume.Value is { } info && ResumePolicy.ShouldAsk(info))
{
    var choice = await _dialogService.AskResumeAsync(
        Path.GetFileName(filePath), info.LastPosition, cancellationToken);
    if (choice == ResumeChoice.Cancel)
    {
        return;
    }

    if (choice == ResumeChoice.Restart)
    {
        await _historyService.ClearCompletedAsync(filePath, cancellationToken);
    }
    else
    {
        startPosition = info.LastPosition;
    }
}

var subtitle = await _subtitleService.FindAutoSubtitleAsync(filePath, cancellationToken);
var request = new OpenMediaRequest(
    filePath,
    startPosition,
    AutoPlay: true,
    subtitle.IsSuccess ? subtitle.Value : null,
    PreferredAudioLanguage: null);
var result = await _playerService.OpenAsync(request, cancellationToken);
if (result.IsSuccess)
{
    _activeSessionId = result.Value!.MediaSessionId;
    StartProgressRefresh();
    StartHistoryFlush();
}
LastError = result.IsSuccess ? string.Empty : result.UserMessage;
~~~

In OnStateChanged, after the first Playing for the active session:

~~~csharp
if (e.NewState == PlaybackState.Playing &&
    Interlocked.Exchange(ref _startedSessionId, e.MediaSessionId) != e.MediaSessionId)
{
    var snapshot = _playerService.Snapshot;
    if (snapshot.MediaPath is { } path)
    {
        await _historyService.StartPlaybackAsync(
            path, Path.GetFileName(path), snapshot.Duration);
    }
}
~~~

Change OnStateChanged to async void only for this event handler; catch and log exceptions inside it.

- [ ] **Step 6: Add five-second flush loop**

Add to PlayerViewModel:

~~~csharp
private CancellationTokenSource? _historyCts;
private TimeSpan _lastSavedPosition;

private void StartHistoryFlush()
{
    _historyCts?.Cancel();
    _historyCts = new CancellationTokenSource();
    _ = RunHistoryFlushAsync(_historyCts.Token);
}

private async Task RunHistoryFlushAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
    try
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!_settingsService.Current.Player.RememberPosition)
            {
                continue;
            }

            var snapshot = _playerService.Snapshot;
            if (Math.Abs((snapshot.Position - _lastSavedPosition).TotalSeconds) < 2)
            {
                continue;
            }

            await _historyService.SaveProgressAsync(snapshot, cancellationToken);
            _lastSavedPosition = snapshot.Position;
        }
    }
    catch (OperationCanceledException)
    {
    }
}
~~~

Before media switch, Pause, Stop, EndReached, and application exit, cancel _historyCts and await _historyService.FlushAsync(). Flush must use a bounded timeout and must not hold the player command gate.

- [ ] **Step 7: Run tests and build**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlayerViewModelHistoryTests
dotnet build VideoPlayer.sln -c Release
~~~

Expected: PASS and successful Release build.

- [ ] **Step 8: Commit**

~~~powershell
git add VideoPlayer/Services/WpfDialogService.cs VideoPlayer/Interfaces/IDialogService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/ViewModels/PlayerViewModel.cs VideoPlayer/CompositionRoot.cs VideoPlayer.Tests/Unit/ViewModels/PlayerViewModelHistoryTests.cs
git commit -m "feat: wire resume and progress persistence"
~~~


---

### Task 18: Add Screenshot and Media Information

**Files:**
- Create: VideoPlayer/Models/VideoInfo.cs
- Create: VideoPlayer/Helpers/FileNameHelper.cs
- Create: VideoPlayer/Interfaces/IScreenshotService.cs
- Create: VideoPlayer/Services/ScreenshotService.cs
- Create: VideoPlayer/Interfaces/IMediaInfoService.cs
- Create: VideoPlayer/Services/MediaInfoService.cs
- Create: VideoPlayer/Views/VideoInfoView.xaml
- Modify: VideoPlayer/Interfaces/IPlayerService.cs
- Modify: VideoPlayer/Services/PlayerService.cs
- Modify: VideoPlayer/CompositionRoot.cs
- Create: VideoPlayer.Tests/Unit/Services/ScreenshotServiceTests.cs

**Interfaces:**
- Consumes: PlaybackSnapshot, PlayerTrackInfo, AppPaths, ISettingsService, IPlayerService.
- Produces: ScreenshotService.CaptureAsync; MediaInfoService.GetCurrentMediaInfoAsync; IPlayerService.TakeSnapshotAsync.

- [ ] **Step 1: Write failing screenshot naming tests**

Create VideoPlayer.Tests/Unit/Services/ScreenshotServiceTests.cs:

~~~csharp
using VideoPlayer.Configuration;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ScreenshotServiceTests
{
    [Fact]
    public async Task CaptureAsync_RemovesInvalidFileNameCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var player = new PlayerViewModelFixture(1).Player;
            var settings = new SettingsService(paths);
            var service = new ScreenshotService(player, settings, paths);
            var snapshot = new PlaybackSnapshot(
                1, Enums.PlaybackState.Playing, "Movie:Part?.mp4",
                TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1),
                80, false, 1.0, true, true, true, DateTimeOffset.UtcNow);

            var result = await service.CaptureAsync(snapshot);

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(":", Path.GetFileName(result.Value));
            Assert.DoesNotContain("?", Path.GetFileName(result.Value));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

}
~~~

The shared PlayerViewModelFixture.FakePlayerService creates the requested output file in TakeSnapshotAsync.

- [ ] **Step 2: Run tests and verify failure**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~ScreenshotServiceTests
~~~

Expected: FAIL because ScreenshotService does not exist.

- [ ] **Step 3: Implement screenshot path and capture**

Create VideoPlayer/Helpers/FileNameHelper.cs:

~~~csharp
using System.IO;

namespace VideoPlayer.Helpers;

public static class FileNameHelper
{
    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }
}
~~~

Create VideoPlayer/Interfaces/IScreenshotService.cs:

~~~csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IScreenshotService
{
    Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
~~~

Create VideoPlayer/Services/ScreenshotService.cs:

~~~csharp
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class ScreenshotService : IScreenshotService
{
    private readonly IPlayerService _playerService;
    private readonly ISettingsService _settingsService;
    private readonly AppPaths _paths;

    public ScreenshotService(
        IPlayerService playerService,
        ISettingsService settingsService,
        AppPaths paths)
    {
        _playerService = playerService;
        _settingsService = settingsService;
        _paths = paths;
    }

    public async Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!snapshot.HasVideo || snapshot.MediaPath is null)
        {
            return ServiceResult<string>.Failure(
                ErrorCode.SnapshotFailed, "当前没有可截图画面。", "No video surface");
        }

        var configuredDirectory = _settingsService.Current.Screenshot.Directory;
        var screenshotRoot = string.IsNullOrWhiteSpace(configuredDirectory)
            ? _paths.ScreenshotDirectory
            : configuredDirectory;
        var directory = Path.Combine(
            screenshotRoot,
            DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(directory);
        var baseName = FileNameHelper.Sanitize(Path.GetFileNameWithoutExtension(snapshot.MediaPath));
        var fileName = baseName + "_" + DateTime.Now.ToString("HH-mm-ss") + ".png";
        var path = Path.Combine(directory, fileName);
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(
                directory,
                baseName + "_" + DateTime.Now.ToString("HH-mm-ss") + "_" +
                suffix.ToString("00") + ".png");
            suffix++;
        }

        var result = await _playerService.TakeSnapshotAsync(path, cancellationToken);
        return result.IsSuccess
            ? ServiceResult<string>.Success(path)
            : ServiceResult<string>.Failure(
                result.ErrorCode, result.UserMessage, result.TechnicalMessage, result.Exception);
    }
}
~~~

- [ ] **Step 4: Extend and implement IPlayerService snapshot**

Add to IPlayerService:

~~~csharp
Task<ServiceResult> TakeSnapshotAsync(
    string outputPath,
    CancellationToken cancellationToken = default);
~~~

Add to PlayerService:

~~~csharp
public async Task<ServiceResult> TakeSnapshotAsync(
    string outputPath,
    CancellationToken cancellationToken = default)
{
    await _commandGate.WaitAsync(cancellationToken);
    try
    {
        if (_mediaPlayer.VideoTrackCount <= 0)
        {
            return ServiceResult.Failure(
                ErrorCode.InvalidState, "当前没有可截图画面。", "No video track");
        }

        var success = _mediaPlayer.TakeSnapshot(0, outputPath, 0, 0);
        return success
            ? ServiceResult.Success()
            : ServiceResult.Failure(
                ErrorCode.SnapshotFailed, "截图失败。", outputPath);
    }
    finally
    {
        _commandGate.Release();
    }
}
~~~

- [ ] **Step 5: Implement VideoInfo and MediaInfoService**

Create VideoPlayer/Models/VideoInfo.cs:

~~~csharp
namespace VideoPlayer.Models;

public sealed record VideoInfo(
    string FileName,
    string FilePath,
    long FileSizeBytes,
    TimeSpan Duration,
    int? Width,
    int? Height,
    string VideoCodec,
    double? FrameRate,
    string AudioCodec,
    int? SampleRate,
    int? Channels);
~~~

Create VideoPlayer/Interfaces/IMediaInfoService.cs:

~~~csharp
using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IMediaInfoService
{
    Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default);
}
~~~

Create VideoPlayer/Services/MediaInfoService.cs:

~~~csharp
using LibVLCSharp.Shared;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class MediaInfoService : IMediaInfoService
{
    private readonly IPlayerService _playerService;

    public MediaInfoService(IPlayerService playerService)
    {
        _playerService = playerService;
    }

    public Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.MediaPath is not { } path)
        {
            return Task.FromResult(ServiceResult<VideoInfo>.Failure(
                ErrorCode.InvalidState, "没有媒体信息。", "No media"));
        }

        var mediaTracks = _playerService.MediaPlayer?.Media?.Tracks ?? [];
        var video = mediaTracks.FirstOrDefault(track => track.TrackType == MediaTrackType.Video);
        var audio = mediaTracks.FirstOrDefault(track => track.TrackType == MediaTrackType.Audio);
        var videoData = video?.Data.Video;
        var audioData = audio?.Data.Audio;
        var frameRate = videoData is { FrameRateDen: > 0 }
            ? (double)videoData.FrameRateNum / videoData.FrameRateDen
            : null;

        var info = new VideoInfo(
            Path.GetFileName(path),
            path,
            File.Exists(path) ? new FileInfo(path).Length : 0,
            snapshot.Duration,
            videoData?.Width,
            videoData?.Height,
            video?.Codec ?? "未知",
            frameRate,
            audio?.Codec ?? "未知",
            audioData?.Rate,
            audioData?.Channels);
        return Task.FromResult(ServiceResult<VideoInfo>.Success(info));
    }
}
~~~

- [ ] **Step 6: Add the VideoInfoView**

Create VideoPlayer/Views/VideoInfoView.xaml with a two-column Grid:

~~~xml
<UserControl x:Class="VideoPlayer.Views.VideoInfoView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="#1B1B1B" Margin="12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="140" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="文件名" Foreground="#AAAAAA" />
        <TextBlock Grid.Column="1" Text="{Binding FileName}" Foreground="White" />
        <TextBlock Grid.Row="1" Text="路径" Foreground="#AAAAAA" />
        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding FilePath}" Foreground="White" />
        <TextBlock Grid.Row="2" Text="分辨率" Foreground="#AAAAAA" />
        <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding Resolution}" Foreground="White" />
        <TextBlock Grid.Row="3" Text="视频编码" Foreground="#AAAAAA" />
        <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding VideoCodec}" Foreground="White" />
        <TextBlock Grid.Row="4" Text="音频编码" Foreground="#AAAAAA" />
        <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding AudioCodec}" Foreground="White" />
        <TextBlock Grid.Row="5" Text="时长" Foreground="#AAAAAA" />
        <TextBlock Grid.Row="5" Grid.Column="1" Text="{Binding Duration}" Foreground="White" />
    </Grid>
</UserControl>
~~~

- [ ] **Step 7: Register services and add screenshot command**

Register IScreenshotService, IMediaInfoService, and VideoInfoViewModel as singletons. Add ScreenshotCommand to PlayerViewModel and bind Ctrl+Alt+S in Task 19. The command must call ScreenshotService.CaptureAsync(_playerService.Snapshot) and set LastError on failure.

- [ ] **Step 8: Run tests and build**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~ScreenshotServiceTests
dotnet build VideoPlayer.sln -c Release
~~~

Expected: PASS and successful Release build.

- [ ] **Step 9: Commit**

~~~powershell
git add VideoPlayer/Models/VideoInfo.cs VideoPlayer/Helpers/FileNameHelper.cs VideoPlayer/Interfaces/IScreenshotService.cs VideoPlayer/Services/ScreenshotService.cs VideoPlayer/Interfaces/IMediaInfoService.cs VideoPlayer/Services/MediaInfoService.cs VideoPlayer/Views/VideoInfoView.xaml VideoPlayer/Interfaces/IPlayerService.cs VideoPlayer/Services/PlayerService.cs VideoPlayer/ViewModels VideoPlayer/CompositionRoot.cs VideoPlayer.Tests
git commit -m "feat: add screenshot and media information"
~~~


---

### Task 19: Add Fullscreen, Keyboard Shortcuts, and Overlay Controls

**Files:**
- Create: VideoPlayer/Views/FullscreenController.cs
- Create: VideoPlayer/Services/ShortcutService.cs
- Modify: VideoPlayer/MainWindow.xaml
- Modify: VideoPlayer/MainWindow.xaml.cs
- Modify: VideoPlayer/Views/PlayerView.xaml
- Modify: VideoPlayer/Views/PlayerView.xaml.cs
- Modify: VideoPlayer/ViewModels/PlayerViewModel.cs
- Create: VideoPlayer.Tests/Unit/Views/FullscreenControllerTests.cs
- Create: VideoPlayer.Tests/Unit/Services/ShortcutServiceTests.cs

**Interfaces:**
- Consumes: Window, VideoView, PlayerViewModel commands.
- Produces: fullscreen enter/exit; keyboard action mapping; control-bar auto hide.

- [ ] **Step 1: Write failing fullscreen state tests**

Create VideoPlayer.Tests/Unit/Views/FullscreenControllerTests.cs:

~~~csharp
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class FullscreenControllerTests
{
    [Fact]
    public void BuildState_CapturesWindowValues()
    {
        var state = FullscreenState.From(
            System.Windows.WindowStyle.SingleBorderWindow,
            System.Windows.ResizeMode.CanResize,
            System.Windows.WindowState.Normal,
            new System.Windows.Rect(10, 20, 800, 600));

        Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, state.Style);
        Assert.Equal(800, state.Bounds.Width);
    }
}
~~~

- [ ] **Step 2: Run test and verify failure**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~FullscreenControllerTests
~~~

Expected: FAIL because FullscreenController does not exist.

- [ ] **Step 3: Implement FullscreenController**

Create VideoPlayer/Views/FullscreenController.cs:

~~~csharp
using System.Windows;

namespace VideoPlayer.Views;

public sealed record FullscreenState(
    WindowStyle Style,
    ResizeMode ResizeMode,
    WindowState WindowState,
    Rect Bounds)
{
    public static FullscreenState From(
        WindowStyle style,
        ResizeMode resizeMode,
        WindowState windowState,
        Rect bounds)
        => new(style, resizeMode, windowState, bounds);
}

public sealed class FullscreenController
{
    private readonly Window _window;
    private FullscreenState? _saved;

    public FullscreenController(Window window)
    {
        _window = window;
    }

    public bool IsFullscreen => _saved is not null;

    public void Toggle()
    {
        if (IsFullscreen)
        {
            Exit();
        }
        else
        {
            Enter();
        }
    }

    public void Enter()
    {
        if (IsFullscreen)
        {
            return;
        }

        _saved = FullscreenState.From(
            _window.WindowStyle,
            _window.ResizeMode,
            _window.WindowState,
            new Rect(_window.Left, _window.Top, _window.Width, _window.Height));
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.WindowState = WindowState.Maximized;
    }

    public void Exit()
    {
        if (_saved is not { } state)
        {
            return;
        }

        _window.WindowStyle = state.Style;
        _window.ResizeMode = state.ResizeMode;
        _window.WindowState = state.WindowState;
        _window.Left = state.Bounds.Left;
        _window.Top = state.Bounds.Top;
        _window.Width = state.Bounds.Width;
        _window.Height = state.Bounds.Height;
        _saved = null;
    }
}
~~~

- [ ] **Step 4: Define and test shortcut actions**

Create VideoPlayer/Services/ShortcutService.cs:

~~~csharp
using System.Windows.Input;

namespace VideoPlayer.Services;

public enum ShortcutAction
{
    None,
    TogglePlayPause,
    SeekBackward5,
    SeekForward5,
    SeekBackward30,
    SeekForward30,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    RateDown,
    RateUp,
    RateReset,
    ToggleFullscreen,
    ExitFullscreen,
    OpenFile,
    OpenFolder,
    TogglePlaylist,
    VideoInfo,
    Screenshot
}

public static class ShortcutService
{
    public static ShortcutAction Resolve(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F11) return ShortcutAction.ToggleFullscreen;
        if (key == Key.Escape) return ShortcutAction.ExitFullscreen;
        if (key == Key.Space) return ShortcutAction.TogglePlayPause;
        if (key == Key.Left) return modifiers.HasFlag(ModifierKeys.Shift)
            ? ShortcutAction.SeekBackward30
            : ShortcutAction.SeekBackward5;
        if (key == Key.Right) return modifiers.HasFlag(ModifierKeys.Shift)
            ? ShortcutAction.SeekForward30
            : ShortcutAction.SeekForward5;
        if (key == Key.Up) return ShortcutAction.VolumeUp;
        if (key == Key.Down) return ShortcutAction.VolumeDown;
        if (key == Key.M) return ShortcutAction.ToggleMute;
        if (key == Key.O && modifiers.HasFlag(ModifierKeys.Control))
            return modifiers.HasFlag(ModifierKeys.Shift)
                ? ShortcutAction.OpenFolder
                : ShortcutAction.OpenFile;
        if (key == Key.P && modifiers.HasFlag(ModifierKeys.Control))
            return ShortcutAction.TogglePlaylist;
        if (key == Key.OemOpenBrackets) return ShortcutAction.RateDown;
        if (key == Key.OemCloseBrackets) return ShortcutAction.RateUp;
        if (key == Key.OemPipe) return ShortcutAction.RateReset;
        if (key == Key.I && modifiers.HasFlag(ModifierKeys.Control)) return ShortcutAction.VideoInfo;
        if (key == Key.S && modifiers.HasFlag(ModifierKeys.Control) &&
            modifiers.HasFlag(ModifierKeys.Alt)) return ShortcutAction.Screenshot;
        return ShortcutAction.None;
    }
}
~~~

Add tests covering Space, Left, Shift+Right, F11, Escape, M, brackets, Ctrl+I, and Ctrl+Alt+S.

- [ ] **Step 5: Wire keyboard and fullscreen behavior**

Add a PreviewKeyDown handler to MainWindow that calls ShortcutService.Resolve and invokes the matching PlayerViewModel command. Mark handled true for consumed keys. Do not route shortcuts when a TextBox has keyboard focus.

Create the FullscreenController in MainWindow after InitializeComponent. F11 toggles, Escape only exits. PlayerView control bar must be inside VideoView.Content so it is visible over the HWND video surface.

Add a DispatcherTimer with a 2 second interval that hides the control bar when the mouse is idle. Mouse move or button press shows the bar and restarts the timer.

- [ ] **Step 6: Run tests and build**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter "FullyQualifiedName~FullscreenControllerTests|FullyQualifiedName~ShortcutServiceTests"
dotnet build VideoPlayer.sln -c Release
~~~

Expected: PASS and successful Release build.

- [ ] **Step 7: Commit**

~~~powershell
git add VideoPlayer/Views/FullscreenController.cs VideoPlayer/Services/ShortcutService.cs VideoPlayer/MainWindow.xaml VideoPlayer/MainWindow.xaml.cs VideoPlayer/Views/PlayerView.xaml VideoPlayer/Views/PlayerView.xaml.cs VideoPlayer/ViewModels/PlayerViewModel.cs VideoPlayer/Tests
git commit -m "feat: add fullscreen and keyboard shortcuts"
~~~

---

### Task 20: Finish Theme, Empty/Error States, and UI Performance Constraints

**Files:**
- Create: VideoPlayer/Resources/Styles/Colors.xaml
- Create: VideoPlayer/Resources/Styles/Controls.xaml
- Modify: VideoPlayer/App.xaml
- Modify: VideoPlayer/Views/PlayerView.xaml
- Modify: VideoPlayer/Views/PlaylistView.xaml
- Create: VideoPlayer.Tests/Integration/Playlist/PlaylistPerformanceTests.cs

**Interfaces:**
- Consumes: existing Views and ViewModels.
- Produces: complete dark theme; empty, loading, and error states; virtualized 10,000-item playlist verification.

- [ ] **Step 1: Add failing performance regression test**

Create VideoPlayer.Tests/Integration/Playlist/PlaylistPerformanceTests.cs that:

1. Creates a temporary directory containing 10,000 empty supported-extension files.
2. Runs PlaylistService.AddFolderAsync.
3. Asserts completion in less than 10 seconds.
4. Asserts the resulting item count is 10,000.
5. Runs a second pass and asserts no duplicates.

Use Stopwatch for elapsed time. Skip the test only when the filesystem reports fewer than 10,000 successfully created files.

- [ ] **Step 2: Run the test and record the current result**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistPerformanceTests --logger "trx;LogFileName=playlist-before.trx"
~~~

Expected: If current code is already optimized, PASS. Otherwise, FAIL and preserve the result file.

- [ ] **Step 3: Add the dark theme**

Create Colors.xaml and Controls.xaml with:

- Background: #111111
- Surface: #1B1B1B
- Primary text: #FFFFFF
- Secondary text: #AAAAAA
- Error: #FF6B6B
- Accent: #4C9AFF
- Button, Slider, ComboBox, ListBoxItem and TextBox styles
- Focus visuals with a visible accent border
- No gradients, blur effects, or complex animation

Merge both dictionaries in App.xaml.

- [ ] **Step 4: Add empty, loading and error states**

PlayerView must display:

- Empty state when no media is open: 打开视频文件或文件夹
- Loading state while PlaybackState is Opening
- Error state when LastError is not empty
- Screenshot success message for at least 2 seconds

PlaylistView must display 播放列表为空 when Items.Count is zero.

- [ ] **Step 5: Verify virtualization**

In PlaylistView, confirm:

~~~xml
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
ScrollViewer.CanContentScroll="True"
~~~

Do not place the ListBox in a parent that measures children with infinite height.

- [ ] **Step 6: Re-run the performance test and compare**

~~~powershell
dotnet test VideoPlayer.Tests/VideoPlayer.Tests.csproj --filter FullyQualifiedName~PlaylistPerformanceTests --logger "trx;LogFileName=playlist-after.trx"
~~~

Expected: PASS with no more than 10 percent regression from the pre-theme result.

- [ ] **Step 7: Commit**

~~~powershell
git add VideoPlayer/Resources VideoPlayer/App.xaml VideoPlayer/Views/PlayerView.xaml VideoPlayer/Views/PlaylistView.xaml VideoPlayer.Tests/Integration/Playlist
git commit -m "feat: finish theme and UI states"
~~~


---

### Task 21: Add Acceptance Harness, Packaging, and the Final 8-Hour Gate

**Files:**
- Create: scripts/acceptance/Invoke-Acceptance.ps1
- Create: scripts/acceptance/Publish-Release.ps1
- Create: scripts/acceptance/Start-SoakTest.ps1
- Create: scripts/acceptance/Test-Architecture.ps1
- Create: docs/acceptance/manual-matrix.md
- Create: docs/acceptance/performance-template.csv
- Create: LICENSES/README.md
- Modify: VideoPlayer.Tests/VideoPlayer.Tests.csproj
- Modify: VideoPlayer/VideoPlayer.csproj

**Interfaces:**
- Consumes: the complete application and test suite.
- Produces: reproducible build/test evidence; folder publish; 8-hour soak procedure; final release gate.

- [ ] **Step 1: Add coverage instrumentation**

Run:

~~~powershell
dotnet add VideoPlayer.Tests/VideoPlayer.Tests.csproj package coverlet.msbuild --version 10.0.1
~~~

Add CopyLocalLockFileAssemblies true to VideoPlayer.csproj.

- [ ] **Step 2: Create the architecture gate**

Create scripts/acceptance/Test-Architecture.ps1:

~~~powershell
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
$files = Get-ChildItem "$root/VideoPlayer/Views","$root/VideoPlayer/ViewModels" -Filter *.cs -Recurse
$forbidden = @(
  'MediaPlayer.Play(',
  'MediaPlayer.Pause(',
  'MediaPlayer.Stop(',
  'MediaPlayer.Time',
  'MediaPlayer.SetAudioTrack(',
  'MediaPlayer.SetSpu('
)
$violations = @()
foreach ($file in $files) {
  $content = Get-Content -Raw $file.FullName
  foreach ($token in $forbidden) {
    if ($content.Contains($token)) { $violations += "$($file.FullName): $token" }
  }
}
if ($violations.Count -gt 0) { $violations | Write-Error; exit 1 }
Write-Host 'Architecture gate passed.'
~~~

- [ ] **Step 3: Create the acceptance runner**

Create scripts/acceptance/Invoke-Acceptance.ps1:

~~~powershell
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
$output = Join-Path $root 'artifacts/acceptance'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$commands = Join-Path $output 'commands.txt'
$build = Join-Path $output 'build-result.txt'
$tests = Join-Path $output 'test-results.trx'

'Building Release...' | Tee-Object $commands
dotnet restore "$root/VideoPlayer.sln" | Tee-Object -Append $commands
dotnet build "$root/VideoPlayer.sln" -c Release --no-restore | Tee-Object -Append $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

'Running tests...' | Tee-Object -Append $commands
dotnet test "$root/VideoPlayer.sln" -c Release --no-build --logger "trx;LogFileName=$tests"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& "$PSScriptRoot/Test-Architecture.ps1"
~~~

- [ ] **Step 4: Create the release publisher**

Create scripts/acceptance/Publish-Release.ps1:

~~~powershell
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/../.."
$output = Join-Path $root 'artifacts/publish/win-x64'
Remove-Item -Recurse -Force $output -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish "$root/VideoPlayer/VideoPlayer.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:ReadyToRun=false -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item "$root/LICENSES" $output -Recurse -Force
Write-Host "Published to $output"
~~~

- [ ] **Step 5: Create the soak monitor**

Create scripts/acceptance/Start-SoakTest.ps1. It must start the published VideoPlayer.exe, accept a media folder, record Working Set, Private Memory, Handle Count, Thread Count and GDI objects once per minute, write artifacts/acceptance/soak.csv, stop after 8 hours or when the user closes the process, and operate only on the process object returned by Start-Process.

- [ ] **Step 6: Create the manual matrix**

Create docs/acceptance/manual-matrix.md with columns: Case ID, Media format or path, Expected result, Actual result, Evidence path, Pass or Fail, Notes.

Add rows for every documented container, path case, shortcut, fullscreen, subtitle, audio track, screenshot, resume case, corrupt database, corrupt config, 20 bad files, 10,000-item playlist, and the 8-hour soak.

- [ ] **Step 7: Create the performance template**

Create docs/acceptance/performance-template.csv with columns: Run, ColdStartSeconds, WarmStartSeconds, FirstFrameSeconds, SeekRecoverySeconds, FullscreenSeconds, WorkingSetMB, Threads, Handles, Notes.

- [ ] **Step 8: Add license notices**

Create LICENSES/README.md containing the exact package names and versions from the V1.1 specification. At publish time copy package license files when provided and include the LibVLC LGPL notice. Never omit a required license notice.

- [ ] **Step 9: Run automated acceptance**

~~~powershell
./scripts/acceptance/Invoke-Acceptance.ps1
~~~

Expected: Release build succeeds, all tests pass, architecture gate passes, commands.txt exists, build-result.txt exists, and test-results.trx exists.

- [ ] **Step 10: Publish and launch**

~~~powershell
./scripts/acceptance/Publish-Release.ps1
Start-Process ./artifacts/publish/win-x64/VideoPlayer.exe
~~~

Verify that the published process opens an MP4 using a Chinese path and closes without an unhandled exception.

- [ ] **Step 11: Execute the 8-hour soak**

~~~powershell
./scripts/acceptance/Start-SoakTest.ps1 -MediaFolder "D:\AcceptanceMedia"
~~~

During the soak, manually execute Seek, pause, resume, format changes, fullscreen, screenshot, previous and next. At completion, compare the final working set with the 10-minute baseline and attach soak.csv to the report.

- [ ] **Step 12: Commit**

~~~powershell
git add scripts/acceptance docs/acceptance LICENSES VideoPlayer.Tests/VideoPlayer.Tests.csproj VideoPlayer/VideoPlayer.csproj
git commit -m "test: add acceptance release and soak gates"
~~~

---

## Phase Gates

| V1.0 Phase | Plan Tasks | Required Evidence |
|---|---|---|
| Phase 1 | 1-8 | Release build; unit tests; player lifecycle; VideoView smoke test |
| Phase 2 | 9-10 | State transitions; seek boundaries; progress timer; playback smoke |
| Phase 3 | 11-13 | Deduplication; no-wrap navigation; auto-next; virtualization |
| Phase 4 | 14 | SRT/ASS/SSA discovery; track switching failure safety |
| Phase 5 | 15-17 | Migration; upsert; resume; first-Playing count; corrupt DB recovery |
| Phase 6 | 18-19 | Screenshot; media information; fullscreen; shortcuts |
| Phase 7 | 20 | Theme; empty/error/loading states; virtualization regression |
| Phase 8 | 21 | Release package; format/path matrices; 8-hour soak; evidence |

---

## Spec Coverage Review

| V1.1 Spec Area | Implementing Tasks |
|---|---|
| Normative precedence and technology baseline | 1, 7, 21 |
| Enums, records, results and state model | 2, 3 |
| Interface contracts | 4, 6, 8, 10, 11, 13-18 |
| Event orchestration | 8, 10, 13, 14, 17 |
| Threading and races | 3, 8-10, 13, 15-17 |
| Persistence and recovery | 5, 6, 15-17 |
| Quantified acceptance | 5, 6, 11, 12, 15-21 |
| Phase gates and Definition of Done | Every task's test, build and commit steps |

---

## Review Focus Tests

1. Late events after rapid navigation are covered by Task 13.
2. Database lock and corruption are covered by Tasks 15 and 16.
3. Unicode, special-character and long-path handling are covered by Tasks 1, 4, 11, 15 and 21.
4. Unsupported media, absent video surfaces and unavailable tracks are covered by Tasks 8, 10, 14 and 18.
5. Shutdown during asynchronous refresh and flush is covered by Tasks 10, 16, 17 and 21.

---

## Execution Rules

- Execute tasks in order.
- Do not start a task until the previous task has a passing test run and a commit.
- If an interface changes, update all consumers and this plan before continuing.
- If a third-party API differs, inspect the installed package metadata or source and update the task and tests first.
- Never commit bin or obj output.
- Never commit test media without confirming redistribution rights.
- Before release, run the full acceptance harness and archive all evidence.
