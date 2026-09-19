# VideoPlayer V1.1 工程补充规范

> 规范状态：Codex 强制执行  
> 基础文档：VideoPlayer V1.0 Codex 开发规范书  
> 基础文档路径：`docs/specs/VideoPlayer-V1.0-Codex-开发规范书.md`  
> 适用范围：Windows 本地视频播放器 V1.0  
> 文档日期：2026-09-20  
> 规范性质：工程契约、并发规则、持久化规则和验收标准补充

---

## 0. 规范优先级与术语

本文件是原《VideoPlayer V1.0 Codex 开发规范书》的强制补充规范。

当本文件与原 V1.0 文档存在冲突时，以本文件为准。原文档继续定义产品功能范围和 UI 目标，本文件定义 Codex 实现和验收时必须遵守的工程契约。

本文件使用以下规范性术语：

- **MUST / 必须**：实现和验收的强制要求，不满足即视为当前 Phase 未完成。
- **SHOULD / 应当**：默认必须遵守；如需偏离，必须记录原因和替代验证结果。
- **MAY / 可以**：允许的可选实现，不得用可选项替代强制要求。

本文件不增加 V1.0 的产品功能范围。允许的工程结构调整只有：

1. 生产项目保持一个 WPF 项目。
2. 增加一个独立的 `VideoPlayer.Tests` 测试项目。
3. 增加测试所需的测试数据、脚本和验收记录。
4. 增加实现本文件契约所需的内部接口、模型和帮助器。

最终解决方案结构必须为：

```text
VideoPlayer.sln
├── VideoPlayer
└── VideoPlayer.Tests
```

禁止创建生产 Class Library、独立 Infrastructure 项目、微服务或其他生产项目。

---

## 1. 固定技术基线

### 1.1 目标平台

生产项目必须使用：

```text
TargetFramework: net10.0-windows
PlatformTarget: x64
RuntimeIdentifier: win-x64
Nullable: enable
ImplicitUsings: enable
UseWPF: true
LangVersion: latest
```

必须以 Windows x64 进程运行。不得使用 AnyCPU 运行含 LibVLC 原生依赖的 WPF 进程。

V1.0 的目标环境为：

- Windows 10 22H2 x64
- Windows 11 x64
- .NET Desktop Runtime 10 x64，或自包含发布包

发布方式必须采用普通文件夹发布：

```text
PublishSingleFile=false
PublishTrimmed=false
ReadyToRun=false
```

项目根目录必须提交 `global.json`，固定 SDK `10.0.401`，`rollForward` 使用 `latestPatch`。生产项目必须启用 NuGet lock file。

应用 manifest 必须启用 `longPathAware`。只有在实际验证 LibVLC 能正常打开后，才允许将 `\\?\` 长路径传给播放器；不得未经测试直接将所有路径加工成长路径前缀。

不得对 WPF、EF Core 或 LibVLC 启用裁剪。禁止依赖未经验证的单文件原生 DLL 解包行为。

### 1.2 第三方依赖版本

本次工程基线使用以下稳定版本，项目文件必须显式固定版本，并提交 `packages.lock.json`：

```text
LibVLCSharp.WPF                         3.10.1
VideoLAN.LibVLC.Windows                 3.0.23.1
CommunityToolkit.Mvvm                   8.4.2
Microsoft.Extensions.DependencyInjection 10.0.12
Microsoft.EntityFrameworkCore.Sqlite    10.0.12
Serilog                                 4.4.0
Serilog.Sinks.File                      7.0.0
```

测试项目使用与目标框架兼容的当前稳定 xUnit、Microsoft.NET.Test.Sdk 和覆盖率工具。第三方库 API 必须以实际安装版本的元数据、示例或源码为准，不得根据记忆猜测。

### 1.3 发布与许可证

发布包必须包含 LibVLC 及其运行所需原生文件。必须保留 LibVLC、VLC 和第三方依赖的许可证及版权声明。V1.0 至少提供：

- 文件夹发布包
- `LICENSES` 目录
- 应用启动日志
- 可选的 ZIP 打包脚本

不得把 GPL 模块静态链接进应用，不得删除原生库附带的通知文件。

---

## 2. 数据模型与枚举契约

### 2.1 播放状态

保留原 `PlaybackState` 值：

```csharp
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

状态含义：

- `None`：服务已初始化但尚未打开媒体。
- `Opening`：当前会话已建立，正在等待 LibVLC 打开或解析媒体。
- `Playing`：媒体正在播放。
- `Paused`：媒体已暂停但保留当前位置。
- `Stopped`：用户主动停止，当前媒体和位置可以被清除或重置。
- `Ended`：当前媒体自然播放结束。
- `Error`：当前会话发生可报告错误。

状态不得通过多个布尔属性组合表达。UI 可以派生 `CanPlay`、`CanPause`、`CanSeek` 等显示属性，但唯一真实状态来源必须是 `PlaybackState`。

### 2.2 状态转换表

只允许以下转换：

```text
None      -> Opening
Opening   -> Playing | Stopped | Error
Playing   -> Paused | Stopped | Ended | Error
Paused    -> Playing | Stopped | Error
Stopped   -> Opening
Ended     -> Opening
Error     -> Opening
```

以下事件不会改变状态：

- Position、Duration、Volume、Mute、Rate 更新。
- 轨道列表更新。
- 自动字幕加载成功或失败。
- 截图失败。
- 媒体信息解析失败。

不允许的状态转换必须被忽略并记录一次结构化警告，不得抛出未处理异常。

### 2.3 不可变播放快照

`IPlayerService` 对外提供不可变快照：

```csharp
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

快照只能整体替换，禁止外部代码修改内部字段。`Position` 和 `Duration` 必须受到有效范围保护；未知时长使用 `TimeSpan.Zero`。

### 2.4 轨道模型

```csharp
public enum PlayerTrackType
{
    Audio,
    Subtitle
}

public sealed record PlayerTrackInfo(
    PlayerTrackType Type,
    int Id,
    string Name,
    string Language,
    bool IsSelected,
    bool IsExternal);
```

名称和语言为空时，必须生成稳定的回退显示名，例如：

```text
音轨 1 (und)
字幕 2 (und)
```

### 2.5 打开媒体请求

```csharp
public sealed record OpenMediaRequest(
    string FilePath,
    TimeSpan StartPosition,
    bool AutoPlay,
    string? ExternalSubtitlePath,
    string? PreferredAudioLanguage);

public sealed record MediaSessionInfo(
    long MediaSessionId,
    string FilePath,
    bool AutoPlay,
    TimeSpan RequestedStartPosition,
    DateTimeOffset CreatedAtUtc);
```

`PlayerService` 负责应用 `StartPosition`、附加外部字幕和选择偏好音轨。调用 ViewModel 不得直接设置 `MediaPlayer.Time` 或调用 LibVLC 轨道 API。

### 2.6 用户选择模型

```csharp
public enum ResumeChoice
{
    Continue,
    Restart,
    Cancel
}
```

`Cancel` 表示取消本次打开请求，不增加播放次数，不修改已有历史完成状态。

### 2.7 结果与错误码

预期内的用户、文件、状态和资源错误不得通过异常穿透到 ViewModel。统一定义：

```csharp
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

public sealed record ServiceResult(
    bool IsSuccess,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null);

public sealed record ServiceResult<T>(
    bool IsSuccess,
    T? Value,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null);
```

规则：

- 用户可理解的消息放入 `UserMessage`。
- 日志可以使用 `TechnicalMessage` 和 `Exception`。
- 编程错误、DI 配置错误和不可能的非法状态可以继续抛出异常。
- `Superseded` 用于被更新的打开或导航请求覆盖的操作，不算播放失败。
- `Cancelled` 用于应用退出或主动取消，不得显示为错误弹窗。

---

## 3. 接口契约

### 3.1 PlayerService

```csharp
public interface IPlayerService : IAsyncDisposable
{
    MediaPlayer? MediaPlayer { get; }

    bool IsInitialized { get; }

    bool HasMedia { get; }

    PlaybackSnapshot Snapshot { get; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    event EventHandler<MediaOpenedEventArgs>? MediaOpened;

    event EventHandler<MediaParsedEventArgs>? MediaParsed;

    event EventHandler<TracksChangedEventArgs>? TracksChanged;

    event EventHandler<EndReachedEventArgs>? EndReached;

    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PlayAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PauseAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult> StopAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SetVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SetMuteAsync(
        bool isMuted,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SetRateAsync(
        double rate,
        CancellationToken cancellationToken = default);

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

    Task<ServiceResult> TakeSnapshotAsync(
        string outputPath,
        CancellationToken cancellationToken = default);
}
```

#### `MediaPlayer` 属性

`MediaPlayer` 只允许 View 的 `VideoView.MediaPlayer` 绑定使用。View 和 ViewModel 不得通过该属性调用 `Play`、`Pause`、`Stop`、`Seek`、轨道切换或截图方法。

`MediaPlayer` 由 `PlayerService` 创建、持有和释放。`VideoView` 只负责绑定和解绑 HWND；`VideoView.Dispose()` 不得释放 `MediaPlayer`。

#### `OpenAsync` 语义

`OpenAsync` 在命令被串行接收并设置新的 `MediaSessionId` 后返回成功。它不等待首帧播放。

打开流程的最终结果必须通过以下事件之一表达：

- `MediaOpened`：媒体已交给 LibVLC。
- `MediaParsed`：时长、视频、音频和轨道信息可用。
- `PlaybackFailed`：打开失败或当前会话失败。

快速切集时，旧会话允许短暂产生事件，但必须因 `MediaSessionId` 不匹配而丢弃。

#### `SeekAsync` 语义

- 无媒体时返回 `InvalidState`。
- `IsSeekable=false` 时返回 `SeekNotAvailable`。
- 小于 0 的值必须限制为 0。
- 大于 Duration 的值必须限制为 Duration。
- Seek 成功表示命令已排队，不承诺当前位置立即等于目标值。
- ViewModel 在收到新的 Position 快照前保留目标值，避免 Slider 抖动。

#### 轨道与字幕方法

- 音轨或字幕轨不存在时返回 `TrackNotFound`。
- `SelectSubtitleTrackAsync(null)` 表示关闭字幕。
- 外部字幕通过 `AddExternalSubtitleAsync` 附加。
- 字幕加载或切换失败不得改变播放状态。

#### `TakeSnapshotAsync`

- 无视频画面时返回 `InvalidState`。
- 输出目录不存在时由调用方先创建，或由 `ScreenshotService` 负责创建。
- 返回成功后文件必须存在且大小大于 0。
- 文件写入必须离开 UI 线程。

### 3.2 事件参数

所有事件参数必须包含 `MediaSessionId`：

```csharp
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public PlaybackState OldState { get; init; }
    public PlaybackState NewState { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class MediaOpenedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class MediaParsedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}

public sealed class TracksChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}

public sealed class EndReachedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class PlaybackFailedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public ErrorCode ErrorCode { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string TechnicalMessage { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
```

事件参数创建后必须不可变。不得把 LibVLC 的回调对象直接传给 ViewModel。

### 3.3 IUiDispatcher

所有对外事件和 UI 状态更新通过以下接口调度：

```csharp
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    Task InvokeAsync(Action action);

    Task<T> InvokeAsync<T>(Func<T> action);
}
```

生产实现包装 WPF `Dispatcher`。测试实现可以立即执行或控制执行顺序，用于复现过期事件和竞态。

### 3.4 PlaylistService

```csharp
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

    Task<ServiceResult> ClearAsync(
        CancellationToken cancellationToken = default);

    ServiceResult<PlaylistItem> GetNext(bool wrap = false);

    ServiceResult<PlaylistItem> GetPrevious(bool wrap = false);

    void SetCurrent(string filePath);
}
```

`PlaylistService` 不依赖 `IPlayerService`。实际播放由 `PlayerViewModel` 发起。

### 3.5 SubtitleService

```csharp
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

自动字幕规则：

- 只查找与媒体相同的基名。
- 扩展名不区分大小写。
- 候选优先级固定为 `.srt`、`.ass`、`.ssa`。
- 多个候选按扩展名优先级和稳定文件名排序。
- 自动查找失败不阻止视频打开。

### 3.6 MediaInfoService

```csharp
public interface IMediaInfoService
{
    Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default);
}
```

媒体信息字段不可用时显示“未知”，不得伪造。该服务不得创建第二个 `MediaPlayer`。

### 3.7 ScreenshotService

```csharp
public interface IScreenshotService
{
    Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
```

该服务负责生成安全路径和调用 `IPlayerService.TakeSnapshotAsync`。截图失败不得影响播放。

### 3.8 HistoryService

```csharp
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

    Task<ServiceResult> FlushAsync(
        CancellationToken cancellationToken = default);
}
```

`HistoryService` 只接受进度快照，不查询 `PlayerService`，也不订阅播放器事件。

### 3.9 SettingsService

```csharp
public interface ISettingsService
{
    AppSettings Current { get; }

    Task<ServiceResult> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveImmediateAsync(
        CancellationToken cancellationToken = default);
}
```

### 3.10 FileService

```csharp
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

目录扫描必须使用 `Task.Run` 或真正的异步枚举，不得在 UI 线程执行大目录遍历。

### 3.11 DialogService

```csharp
public interface IDialogService
{
    Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default);

    Task ShowErrorAsync(
        string title,
        string userMessage,
        CancellationToken cancellationToken = default);

    Task ShowInformationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
```

Service 不得直接调用 `MessageBox`。所有用户交互通过 ViewModel 和 `IDialogService` 完成。

---

## 4. 事件编排

### 4.1 编排职责

`PlayerViewModel` 是唯一的播放编排层。它负责：

- 接收用户命令。
- 读取播放列表目标。
- 调用字幕、历史、配置和播放器服务。
- 控制自动下一项。
- 在 UI 线程更新显示状态。
- 处理错误和用户提示。

`PlayerService` 只生成事实事件，不决定自动下一项，不读写历史，不读取播放列表。

`PlaylistService`、`HistoryService`、`SubtitleService` 和 `SettingsService` 不得互相订阅事件。避免通过事件形成隐式双向依赖。

### 4.2 事件定义

| 事件 | 触发时机 | 主要消费者 | 是否允许重复 |
|---|---|---|---|
| `StateChanged` | 状态机实际转换后 | PlayerViewModel | 相同状态不触发 |
| `MediaOpened` | LibVLC 已接收当前媒体 | PlayerViewModel | 每个会话最多一次 |
| `MediaParsed` | 时长和轨道信息可用 | PlayerViewModel、MediaInfoService | 每个会话有限次，合并最新结果 |
| `TracksChanged` | 音轨或字幕轨集合变化 | PlayerViewModel | 允许重复，必须带完整最新集合 |
| `EndReached` | 当前媒体自然结束 | PlayerViewModel | 每个会话最多一次 |
| `PlaybackFailed` | 当前媒体无法继续播放并进入 Error | PlayerViewModel | 每个会话最多一次 |

事件的名义顺序是 `MediaOpened -> MediaParsed -> Playing`，但 LibVLC 实际回调顺序可能变化。状态机必须根据当前状态和 `MediaSessionId` 接受合法事件，不能依赖回调绝对顺序。

### 4.3 打开媒体时序

打开媒体必须按照以下顺序执行：

```text
用户或系统请求打开
-> 获取 ViewModel 编排锁
-> Flush 旧媒体进度
-> FileService 规范化并验证路径
-> HistoryService 查询续播信息
-> SubtitleService 自动查找字幕
-> 必要时询问续播方式
-> 构造 OpenMediaRequest
-> PlayerService.OpenAsync
-> PlayerService 生成新 MediaSessionId
-> 状态进入 Opening
-> MediaOpened
-> MediaParsed
-> 首次 Playing
-> 应用一次续播位置
-> HistoryService.StartPlaybackAsync，PlayCount + 1
-> 启动进度 Snapshot 刷新
```

规则：

- 旧媒体进度必须在切换前 Flush。
- 用户选择“从头播放”时，`OpenMediaRequest.StartPosition = TimeSpan.Zero`。
- 用户选择“继续播放”时，起始位置不得超过 `Duration - 1s`。
- 播放次数只在首次进入 `Playing` 后增加。
- 打开失败不得增加播放次数。
- 打开新会话后，旧会话的所有事件必须丢弃。

### 4.4 播放、暂停和停止时序

播放请求：

```text
Validate State 和 HasMedia
-> PlayerService.PlayAsync
-> StateChanged(Playing)
-> 启动或恢复进度 Timer
```

暂停请求：

```text
Validate State
-> Flush 当前进度
-> PlayerService.PauseAsync
-> StateChanged(Paused)
-> 降低进度刷新频率
```

停止请求：

```text
Validate State
-> PlayerService.StopAsync
-> Flush 当前进度
-> StateChanged(Stopped)
-> Position 归零
-> 停止进度 Timer
```

停止不得自动删除当前列表项。停止后再次播放的语义由当前媒体是否仍存在决定；若已无媒体，返回 `InvalidState`。

### 4.5 手动上一项和下一项

```text
Flush 当前进度
-> PlaylistService.GetPrevious/GetNext
-> 目标不存在则保持当前项并返回 InvalidState，不循环
-> 递增 NavigationVersion
-> 调用统一打开流程
-> 旧 Open 结果若晚到则返回 Superseded
```

快速连续请求必须满足：

- 最终选择由最后一次用户操作决定。
- 旧会话的 `MediaOpened`、`Playing`、`Error` 和 `EndReached` 不得更新当前 UI。
- 每个被覆盖的请求返回 `Superseded`，不得显示错误。
- 播放列表为空或只有一个项目时，上一项和下一项的行为必须明确为 `InvalidState` 或保持当前项，不得自动循环，除非以后显式新增循环设置。

V1.0 固定为不循环：

```text
第一项的上一项 -> 保持第一项并返回 InvalidState
最后一项的下一项 -> 保持最后一项并返回 InvalidState
```

### 4.6 自动下一项

收到 `EndReached` 后执行：

```text
校验 MediaSessionId
-> 原子标记会话已 End
-> Flush 历史并 MarkCompleted
-> 读取 AutoPlayNext
-> false: 状态保持 Ended，不打开新项
-> true: 查询下一项
-> 下一项不存在: 状态保持 Ended
-> 下一项存在: 调用统一打开流程
```

约束：

- 同一个 `MediaSessionId` 的多个 EndReached 结果只能处理一次。
- 自动下一项不得由 `PlayerService` 或 `PlaylistService` 单独完成。
- 最后一项播放结束后不得自动跳回第一项。
- 若历史写入失败，仍允许自动下一项。
- 若自动打开下一项失败，保持 `Error` 并等待用户操作，不继续扫描后续项。

### 4.7 错误编排

当前媒体无法继续播放时必须遵循：

```text
PlayerService 捕获或识别播放路径错误
-> 如果会话已失效则丢弃
-> StateChanged(Error)
-> PlaybackFailed 一次
-> PlayerViewModel 记录日志
-> IDialogService 显示用户消息
-> 用户可选择重试、下一项、停止或关闭
```

以下播放错误进入 `Error` 并发送一次 `PlaybackFailed`：

- 文件不存在。
- 文件无权限。
- 视频格式无法解析。
- 当前媒体打开失败。

以下非播放错误不得发送 `PlaybackFailed`，也不得改变 `PlaybackState`：

- 字幕加载或切换失败。
- 数据库不可用。
- 配置损坏或保存失败。
- 截图失败。
- 媒体信息解析失败。

V1.0 对坏文件不自动跳过。这样可以防止一个包含大量损坏文件的目录在无人操作时快速连续打开失败。

以下错误属于启动核心失败：

- LibVLC 初始化失败。
- MediaPlayer 创建失败。

此时必须停止继续创建播放窗口，显示明确错误并安全退出，禁止让空引用异常穿透到未处理异常处理器。
### 4.8 字幕编排

自动字幕：

1. `SubtitleService.FindAutoSubtitleAsync` 查找候选。
2. 找到后传入 `OpenMediaRequest.ExternalSubtitlePath`。
3. `PlayerService` 在当前媒体设置完成后使用当前 LibVLCSharp API 附加字幕。
4. 附加失败不得阻止播放，只记录日志并显示非阻塞提示。
5. 内嵌字幕通过 `TracksChanged` 获取，和外部字幕合并显示。
6. 外部字幕必须标记 `IsExternal=true`。

手动字幕：

```text
用户选择文件
-> 验证扩展名和可读性
-> PlayerService.AddExternalSubtitleAsync
-> 刷新字幕轨道
-> 选中新字幕
```

关闭字幕：

```text
PlayerService.SelectSubtitleTrackAsync(null)
```

字幕操作不得使用 `View` 直接操作 LibVLC。

### 4.9 音轨编排

- 音轨列表来自 `TracksChanged`。
- 用户选择后调用 `SelectAudioTrackAsync`。
- 选择成功不改变播放状态。
- 选择失败保留原音轨并显示非阻塞提示。
- 默认音轨由 LibVLC 选择；V1.0 不实现复杂语言偏好推断。

### 4.10 媒体信息和截图编排

媒体信息：

1. `MediaParsed` 到达后异步请求 `MediaInfoService`。
2. 结果只填充视频信息窗口。
3. 信息缺失显示“未知”。
4. 解析失败不得阻塞播放和列表操作。

截图：

1. 用户触发截图命令。
2. 检查当前 Snapshot 存在视频画面。
3. `ScreenshotService` 计算路径并调用 `PlayerService.TakeSnapshotAsync`。
4. 插入成功提示或失败提示。
5. 截图不得修改播放状态、历史位置或当前轨道。

### 4.11 启动时序

固定启动顺序：

```text
Application 启动
-> 初始化全局异常日志
-> 创建 AppPaths
-> 加载 SettingsService
-> 构建 DI 容器
-> 初始化数据库；失败则降级 HistoryService
-> 初始化 LibVLC；失败则显示核心启动错误并退出
-> 创建 MediaPlayer；失败则显示核心启动错误并退出
-> 创建 ViewModel 和 MainWindow
-> 应用主题和尺寸
-> 显示窗口
```

`SettingsService` 和 `HistoryService` 是核心播放路径的非硬依赖。任何失败都必须留下日志，但不能阻止播放器窗口打开，除非具体功能本身不可用。

### 4.12 退出时序

固定退出顺序：

```text
停止接收新命令
-> 取消后台 Timer 和媒体会话 Token
-> Flush 当前播放进度
-> 保存配置
-> 停止播放并解绑事件
-> VideoView.Dispose()
-> 释放 MediaPlayer
-> 释放 Media
-> 释放 LibVLC
-> 关闭数据库
-> 关闭日志
```

退出流程必须幂等。重复关闭窗口、应用会话结束或未处理异常退出不得造成二次释放。

---

## 5. 线程模型与竞态规则

### 5.1 线程所有权

| 资源 | 所有者 | 访问规则 |
|---|---|---|
| View 和控件 | WPF UI 线程 | 只在 UI 线程访问 |
| ViewModel 可绑定属性 | UI 线程 | 只通过 `IUiDispatcher` 更新 |
| ObservableCollection | UI 线程 | 禁止后台线程直接修改 |
| LibVLC | PlayerService | 生命周期由 PlayerService 管理 |
| MediaPlayer | PlayerService | 所有修改通过 `_commandGate` |
| Media | PlayerService | 当前会话内唯一使用，切集时释放旧对象 |
| Snapshot | PlayerService | 不可变对象，原子替换 |
| DbContext | HistoryService | 每个操作创建短生命周期上下文 |
| 文件扫描 | 后台线程 | 返回不可变列表后一次性更新 UI |
| 截图保存 | 后台线程 | 完成后通过 UI Dispatcher 通知 |

### 5.2 命令串行化

`PlayerService` 使用一个 `SemaphoreSlim(1, 1)` 保护所有会修改 MediaPlayer 的操作：

```text
OpenAsync
PlayAsync
PauseAsync
StopAsync
SeekAsync
SetVolumeAsync
SetMuteAsync
SetRateAsync
SelectAudioTrackAsync
SelectSubtitleTrackAsync
AddExternalSubtitleAsync
TakeSnapshotAsync
DisposeAsync
```

规则：

- 禁止在持有 `_commandGate` 时 await 数据库、对话框、文件扫描或其他服务。
- 排队命令在获得锁后必须重新检查组件的 `_disposed` 状态。
- 音量、倍速和静音的重复命令可以合并为最新值。
- Seek 命令 250ms 内重复时可以合并。
- Open、Stop、Dispose 不得被静默合并；必须按照会话规则处理覆盖关系。

### 5.3 MediaSessionId 与过期事件

每次 `OpenAsync` 成功接收请求后生成新的 `MediaSessionId`。事件处理器在更新状态或 UI 前必须验证：

```csharp
if (args.MediaSessionId != _activeSessionId)
{
    return; // 旧会话事件，直接丢弃
}
```

以下事件必须进行会话过滤：

- `StateChanged`
- `MediaOpened`
- `MediaParsed`
- `TracksChanged`
- `EndReached`
- `PlaybackFailed`

旧会话事件可以记录 Debug 日志，但不得写入历史、不得更新当前 UI、不得触发自动下一项。

### 5.4 VLC 原生回调规则

LibVLC 回调线程不得执行以下操作：

- 访问 WPF 控件。
- 修改 ObservableCollection。
- 直接更新 ViewModel 属性。
- 调用数据库。
- 等待异步任务。
- 显示 MessageBox。
- 执行大目录扫描。
- 执行文件写入。

回调只允许：

1. 捕获不可变参数。
2. 校验会话编号。
3. 更新 PlayerService 内部状态。
4. 通过 `IUiDispatcher.Post` 调度事件。
5. 捕获所有异常并转换成可记录的失败事件。

原生回调中不得出现未捕获异常。

### 5.5 UI Dispatcher 契约

`PlayerService` 对外事件必须在 UI 线程触发。生产实现使用 `IUiDispatcher`：

- `Post` 用于非等待事件。
- `InvokeAsync` 用于需要在 UI 完成后再继续的更新。
- UI 线程判断使用 `Dispatcher.CheckAccess()`。
- 应用关闭后 Dispatcher 可能拒绝排队；该情况必须被捕获并安全忽略。

不能在 Service 中直接使用 `Application.Current.Dispatcher`，避免测试和非 UI 启动失败。

### 5.6 进度刷新

使用后台 `PeriodicTimer` 生成播放进度快照：

```text
Playing: 200ms
Paused: 500ms
Stopped/Ended/Error: 停止
```

规则：

- 每次 Tick 只读取一次 Snapshot。
- UI 更新必须等待上一次更新完成，禁止 Tick 重叠。
- 位置和时长只在变化时触发 PropertyChanged。
- 禁止逐帧更新 ProgressBar。
- Timer 取消必须在释放 MediaPlayer 之前完成。

### 5.7 UI 集合线程规则

- 扫描结果先在后台形成 `List<PlaylistItem>`。
- 只允许在 UI 线程调用 `ObservableCollection` 修改方法。
- 批量添加必须尽量合并为一次集合通知。
- 10,000 项列表使用虚拟化控件。
- 不得在 `ItemsControl` 的模板中使用会禁用虚拟化的滚动结构。
- 测试模式下如果后台线程修改集合，必须抛出断言错误并在测试中失败。

### 5.8 数据库线程规则

- 禁止跨线程共享一个 `DbContext`。
- 使用 `IDbContextFactory<VideoPlayerDbContext>`。
- 每个查询和写入使用独立短生命周期上下文。
- `HistoryService` 写操作通过 `SemaphoreSlim(1, 1)` 串行化。
- 读取操作可以与播放并行，但不得阻塞 `_commandGate`。
- 数据库不可用时返回 `DatabaseUnavailable`，播放继续。
- 退出 Flush 最多等待 3 秒，超时必须取消等待。

### 5.9 释放竞态

`PlayerService.DisposeAsync` 必须：

1. 使用 `Interlocked.Exchange` 标记 disposed。
2. 取消进度 Timer 和会话 Token。
3. 获取 `_commandGate`，拒绝新命令。
4. 解绑 MediaPlayer 事件。
5. 停止播放。
6. 释放 Media。
7. 释放 MediaPlayer。
8. 释放 LibVLC。
9. 清空事件订阅者。

释放方法必须幂等：

- 第一次调用执行实际释放。
- 第二次调用立即返回成功。
- 不重复调用 `Dispose()`。
- 不在释放后访问 `NativeReference`。

禁止使用 `.Result` 或 `.Wait()` 等待播放器释放。WPF 退出流程应使用有边界的异步关闭流程或明确的安全同步边界。

### 5.10 必测竞态场景

以下场景必须有自动化或可重复的测试：

1. 快速连续下一项 100 次，最终播放最后一个目标。
2. 打开媒体 A 后立即打开媒体 B，延迟触发 A 的事件，B 不受影响。
3. 重复触发 10 次 `EndReached`，只自动打开下一项一次。
4. 暂停、Seek、Stop 和退出同时发生，不访问已释放对象。
5. 数据库锁定时播放和切集不受影响。
6. UI Timer 连续运行两小时，无重叠 Tick 和无界任务增长。
7. 连续 100 次打开和关闭媒体，事件订阅数量不增长。
8. 取消已经排队的命令返回 `Cancelled`，不留下半初始化媒体。

---

## 6. 持久化与恢复

### 6.1 AppPaths

所有路径必须通过 `Environment.SpecialFolder` 和 `Path` API 计算，禁止硬编码用户目录或通过字符串拼接路径。

```text
配置目录：%AppData%\VideoPlayer
数据库：%AppData%\VideoPlayer\VideoPlayer.db
配置：%AppData%\VideoPlayer\appsettings.json
配置备份：%AppData%\VideoPlayer\appsettings.json.bak
日志：%AppData%\VideoPlayer\Logs
截图：%Pictures%\VideoPlayer
```

应用启动时必须创建配置目录和日志目录。截图目录可以在第一次截图时创建。

### 6.2 PlaybackHistory 表

EF Core 实体映射必须等价于：

```sql
CREATE TABLE PlaybackHistory (
    Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    FilePath           TEXT NOT NULL,
    PathKey            TEXT NOT NULL,
    FileName           TEXT NOT NULL,
    DurationTicks      INTEGER NOT NULL DEFAULT 0,
    LastPositionTicks  INTEGER NOT NULL DEFAULT 0,
    LastPlayedAtUtc    TEXT NOT NULL,
    PlayCount          INTEGER NOT NULL DEFAULT 0,
    IsCompleted        INTEGER NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IX_PlaybackHistory_PathKey
    ON PlaybackHistory (PathKey);
```

字段规则：

- `FilePath` 使用 `Path.GetFullPath` 后的绝对路径，用于显示和打开媒体。
- `PathKey` 使用规范化绝对路径的 `ToUpperInvariant()` 结果，用于 Windows 不区分大小写的唯一性判断。
- `PathKey` 必须由唯一的 `PathHelper.CreatePathKey` 生成，PlaylistService 和 HistoryService 不得各自实现不同规则。
- `FileName` 只用于显示，不参与唯一性判断。
- 时长和位置存储为 Ticks，避免 Provider 类型转换歧义。
- `LastPlayedAtUtc` 使用 UTC 时间。
- `IsCompleted` 为布尔值。
- 不允许把 `Duration=0` 当成有效完成依据。

### 6.3 续播决策

`ResumeInfo` 必须包含：

```csharp
public sealed record ResumeInfo(
    string FilePath,
    TimeSpan Duration,
    TimeSpan LastPosition,
    bool IsCompleted,
    DateTimeOffset LastPlayedAtUtc,
    int PlayCount);
```

续播询问必须满足全部条件：

1. `RememberPosition=true`。
2. 文件存在且可读。
3. `IsCompleted=false`。
4. `Duration >= 30s`。
5. `LastPosition >= 15s`。
6. `LastPosition < Duration * 0.95`。
7. `Duration - LastPosition >= 10s`。

不满足条件时：

- `IsCompleted=true` 或位置超过 95%：从 0 开始。
- 位置小于 15 秒：从 0 开始。
- 距离结尾不足 10 秒：从 0 开始。
- 文件已不存在：不显示续播，记录日志。

### 6.4 播放次数规则

- `PlayCount` 只在当前 `MediaSessionId` 第一次进入 `Playing` 时增加一次。
- Play、Pause、Resume、Seek、切换音轨和截图不增加 PlayCount。
- 打开失败、取消或 Superseded 不增加 PlayCount。
- 同一个会话进入 Playing 多次只增加一次。

### 6.5 完成状态规则

满足以下任一条时设置 `IsCompleted=true`：

1. 当前会话收到第一次有效 `EndReached`。
2. `Duration >= 30s` 且 `LastPosition / Duration >= 0.95`。

用户选择从头播放时立即调用 `ClearCompletedAsync`。完成状态只影响续播，不影响文件自身。

### 6.6 进度写入策略

播放期间：

```text
每 5 秒检查一次
-> 位置相对上次保存变化 >= 2 秒
-> 调用 SaveProgressAsync
```

立即 Flush 的时机：

- 用户暂停。
- 用户停止。
- 手动上一项或下一项。
- 自动下一项前。
- `EndReached`。
- 窗口关闭。
- 应用退出。
- 当前媒体发生错误。

数据库写入约束：

- 写操作在后台执行，不得阻塞 UI 线程。
- 所有写入由 `HistoryService` 串行化。
- 只允许最新位置覆盖旧位置。
- 旧会话写入不得在切换媒体后覆盖新位置。
- 写入失败必须记录日志，但不得影响播放。
- 正常退出 Flush 超时为 3 秒。
- 强制终止最多允许丢失最近 7 秒进度。

### 6.7 数据库初始化与损坏恢复

启动流程：

```text
创建 %AppData%\VideoPlayer
-> 检查数据库文件
-> 执行 Database.MigrateAsync
-> 验证能执行最小查询
-> 创建 HistoryService 可用状态
```

数据库无法打开、Migration 失败或最小查询失败时：

1. 记录异常和数据库路径。
2. 尝试关闭现有连接。
3. 将原文件重命名为 `VideoPlayer.corrupt.yyyyMMddHHmmss.db`。
4. 创建新的数据库并执行 Migration。
5. 如果再次失败，`HistoryService.IsAvailable=false`。
6. 应用继续启动，播放功能不受影响。

禁止在恢复失败时反复重试并阻塞启动。数据库恢复只允许在本次启动内尝试一次。

### 6.8 配置文件结构

默认配置必须包含 SchemaVersion：

```json
{
  "SchemaVersion": 1,
  "Player": {
    "DefaultVolume": 80,
    "DefaultPlaybackRate": 1.0,
    "AutoPlayNext": true,
    "RememberPosition": true
  },
  "Subtitle": {
    "AutoLoad": true,
    "Encoding": "Auto"
  },
  "UI": {
    "Theme": "Dark",
    "ShowPlaylist": false
  },
  "Screenshot": {
    "Directory": ""
  }
}
```

配置校验规则：

- 音量限制为 `0..100`。
- 播放倍速限制为 `0.5..2.0`。
- 字幕编码只允许 `Auto`、`UTF-8`、`GB18030`、`UTF-16`。
- 空截图目录回退到 `Pictures\VideoPlayer`。
- 未知字段保留或忽略，不得导致启动失败。
- SchemaVersion 高于当前程序版本时使用默认配置并记录警告。

### 6.9 配置原子写入

保存配置必须使用：

```text
序列化到临时文件
-> Flush
-> 原文件复制为 appsettings.json.bak
-> 临时文件原子替换主文件
-> 删除临时文件
```

禁止直接覆盖主文件。主文件损坏时：

1. 记录错误。
2. 尝试读取 `.bak`。
3. 备份失败时使用默认配置。
4. 启动完成后重新生成有效主文件。

### 6.10 截图命名和保存

截图基名必须移除以下内容：

```text
Path.GetInvalidFileNameChars()
```

命名格式：

```text
视频名_HH-mm-ss.png
```

同一秒发生冲突时：

```text
视频名_HH-mm-ss_01.png
视频名_HH-mm-ss_02.png
```

日期目录使用本地日期，目录结构：

```text
Pictures\VideoPlayer\yyyy-MM-dd\
```

截图路径生成和文件写入必须在 UI 线程之外完成。截图成功后返回绝对路径；失败返回 `SnapshotFailed`。

---

## 7. 量化验收标准

### 7.1 参考验收环境

自动化和性能结果必须在以下参考环境记录：

```text
OS: Windows 10 22H2 x64 或 Windows 11 x64
CPU: 4 物理核心及以上
内存: 8GB 及以上
磁盘: 本地 SSD
显卡: 支持当前 LibVLC 硬件解码
.NET: 10 x64 Desktop Runtime 或自包含发布包
```

每次验收记录必须包含日期、机器配置、应用版本、测试媒体、命令和结果。

### 7.2 构建和测试门槛

以下命令必须成功：

```powershell
dotnet restore VideoPlayer.sln
dotnet build VideoPlayer.sln -c Release
dotnet test VideoPlayer.sln -c Release
```

要求：

- 生产代码编译错误为 0。
- 生产代码 Nullable 警告为 0。
- 新增编译器警告必须有明确豁免记录。
- 测试失败数为 0。
- Service、Model、Helper、StateMachine 行覆盖率不低于 80%。
- 对应分支覆盖率不低于 70%。
- 生产项目通过 `InternalsVisibleTo` 仅向 `VideoPlayer.Tests` 开放测试所需的内部类型。
- 架构测试必须证明 ViewModel 不直接调用播放器播放 API。
- 每个 Phase 结束都必须运行当前相关测试；不得把所有测试推迟到 Phase 8。

### 7.3 启动和交互性能

在参考环境中连续执行 5 次并取最大值，作为保守的 P95 近似值：

| 指标 | 门槛 |
|---|---:|
| 冷启动到窗口可交互 | <= 3 秒 |
| 热启动到窗口可交互 | <= 1.5 秒 |
| 1080p MP4 打开到首帧 | <= 3 秒 |
| Play/Pause/Stop UI 反馈 | <= 200ms |
| Seek UI 反馈 | <= 200ms |
| Seek 后首帧恢复 | <= 1 秒 |
| F11 全屏切换 | <= 500ms |
| Slider 拖动时最大 UI 阻塞 | <= 200ms |

性能测试必须同时记录进程内存、CPU 和是否掉帧。若参考机器达不到某个门槛，必须在验收报告中说明硬件或媒体原因，不得直接忽略指标。

### 7.4 播放列表性能

| 场景 | 门槛 |
|---|---:|
| 扫描并加入 10,000 个视频 | <= 10 秒 |
| 扫描期间单次 UI 操作响应 | <= 500ms |
| 10,000 项列表滚动和选择 | <= 200ms |
| 连续上一项/下一项 100 次 | 最终只保留最后一次结果 |
| 列表操作期间崩溃 | 0 |
| 列表操作期间后台任务持续增长 | 0 |

列表必须启用 UI 虚拟化。不得因为 10,000 项一次性创建全部可视元素。

### 7.5 播放稳定性

必须执行以下测试：

1. 1080p 30fps 文件连续播放 30 分钟。
2. 连续播放至少 4 小时墙钟时间，记录工作集和线程数量。
3. 连续打开和关闭 100 个媒体。
4. 连续播放 20 个无效或缺失文件。
5. 播放期间执行 100 次 Seek、暂停、继续和轨道切换。
6. 播放期间执行窗口移动、最小化、恢复和全屏切换。

通过条件：

- 进程不崩溃。
- UI 不永久无响应。
- 不出现持续黑屏。
- 线程数量不持续增加。
- 托管内存经过 GC 后不持续线性增长。
- 播放 10 分钟基线后，4 小时墙钟时间测试工作集增长不超过 100MB。
- 连续 20 个坏文件后仍能继续操作并播放后续有效文件。
- 任何未处理异常都必须写入日志且不得直接终止进程，除非属于不可恢复的 LibVLC 原生崩溃。

### 7.6 格式验收矩阵

必须覆盖：

```text
MP4
MKV
AVI
MOV
WMV
TS
M2TS
WebM
```

每个格式必须验证：

- 打开。
- 播放。
- 暂停。
- 停止。
- Seek。
- 快进 5 秒和 30 秒。
- 快退 5 秒和 30 秒。
- 音量、静音和倍速。
- 字幕。
- 音轨。
- 关闭应用。

超出 LibVLC 能力的编码可以失败，但必须产生明确错误、写入日志并且不崩溃。

### 7.7 路径验收矩阵

必须测试：

```text
C:\视频\电影\测试.mp4
C:\My Videos\Movie 01.mkv
C:\Videos\[测试] #1 & 最终版.mp4
```

还必须测试：

- 超过 260 字符的路径。
- 同一路径不同大小写。
- 文件被其他进程占用。
- 文件无读取权限。
- 文件在播放中删除。
- 文件在播放中移动。
- 文件夹路径包含中文、空格和特殊字符。

通过条件：

- 路径比较不产生重复历史。
- 路径不因字符串拼接被截断或转义。
- 失败文件不导致应用退出。
- 历史记录不会因为单个文件不可访问而损坏。

### 7.8 持久化验收

| 指标 | 门槛 |
|---|---:|
| 正常退出后续播误差 | <= 1 秒 |
| 强制终止最大进度损失 | <= 7 秒 |
| 10,000 次进度写入后记录数 | 每个路径 1 条 |
| 数据库损坏后启动 | <= 5 秒 |
| 数据库损坏后播放 | 仍可用 |
| 数据库锁定时播放延迟增加 | <= 100ms |
| 配置损坏后回退 | <= 2 秒 |
| 配置损坏后应用启动 | 必须成功 |

数据库恢复和配置回退必须产生日志，但不得弹出阻塞式错误对话框。

### 7.9 8 小时耐久测试

最终发布前必须完成一次至少 8 小时的耐久测试：

- 持续播放有效媒体。
- 每隔固定时间执行 Seek。
- 执行播放、暂停、继续。
- 执行音量、静音和倍速变更。
- 执行全屏切换。
- 执行截图。
- 执行上一项和下一项。
- 执行播放列表添加和删除。

通过条件：

- 无崩溃。
- 无死锁。
- 无持续增长的后台线程。
- 工作集增长不超过 150MB。
- GDI Handle 和 User Handle 不持续线性增长。
- 日志不包含高频 Position 日志。
- 所有异常都有用户可理解的对应状态或提示。

### 7.10 验收证据

每个 Phase 和最终版本必须保留：

```text
acceptance/
├── commands.txt
├── build-result.txt
├── test-results.trx
├── manual-matrix.md
├── performance.csv
└── logs/
```

验收报告不得只写“测试通过”。必须记录以下信息：

- 执行日期和时间。
- 运行环境。
- 使用的媒体文件和路径。
- 执行命令。
- 实际结果。
- 与门槛的差值。
- 失败项、原因和是否阻塞发布。

---

## 8. Phase 门禁

原 V1.0 的 Phase 划分保留，但每个 Phase 增加以下出口条件：

### Phase 1：项目初始化和最小播放

必须通过：

- Solution 和目标框架正确。
- x64 运行。
- LibVLC 和 MediaPlayer 初始化成功。
- VideoView 可以显示视频。
- 打开、播放、暂停、停止可用。
- 中文路径 MP4 可以播放。
- 关闭窗口无未处理异常。
- Release Build 成功。
- 首次生成测试项目和基础架构测试。

### Phase 2：播放器核心

必须通过：

- PlaybackState 转换测试。
- Seek 边界测试。
- 音量、静音和倍速边界测试。
- 命令串行化和重复 Seek 合并测试。
- 进度 Timer 无重叠测试。
- 播放状态 UI 不出现明显卡顿。

### Phase 3：播放列表

必须通过：

- 文件夹扫描和自然排序测试。
- 不区分大小写去重测试。
- 10,000 项性能测试。
- 快速上一项/下一项竞态测试。
- 自动下一项和最后一项不循环测试。

### Phase 4：字幕和音轨

必须通过：

- 自动字幕匹配测试。
- 手动字幕加载测试。
- 关闭字幕测试。
- 内嵌字幕和外部字幕显示测试。
- 音轨切换失败不终止播放测试。
- `TracksChanged` 去重和会话过滤测试。

### Phase 5：播放历史

必须通过：

- Migration 测试。
- 续播决策单元测试。
- 播放次数单次增加测试。
- 完成状态测试。
- 数据库损坏恢复测试。
- 10,000 次写入最终一致性测试。
- 强制终止后最大 7 秒进度损失测试。

### Phase 6：高级播放器功能

必须通过：

- 全屏和 Esc 退出测试。
- 鼠标移动后控制栏显示测试。
- 截图成功和失败测试。
- 视频信息缺失字段显示测试。
- 快捷键不冲突测试。
- 所有高级功能不影响播放状态。

### Phase 7：UI 优化

必须通过：

- 深色主题完整。
- 空状态、错误状态和加载状态可见。
- 控制栏在视频区域内正常显示。
- 10,000 项列表仍保持虚拟化。
- UI 优化前后关键性能指标不得回退超过 10%。
- 不做复杂皮肤和特效。

### Phase 8：最终测试

必须通过：

- 全部格式矩阵。
- 全部路径矩阵。
- 持久化矩阵。
- 8 小时耐久测试。
- Release 发布包启动测试。
- 发布包包含原生依赖和许可证。
- 所有验收证据归档。

---

## 9. Definition of Done

一个 Phase 只有同时满足以下条件才算完成：

1. 代码符合本补充规范和原 V1.0 的产品范围。
2. 没有新增生产项目或不受控依赖。
3. 代码可以 Release 构建。
4. 相关单元测试和集成测试通过。
5. 当前 Phase 的量化指标有实际结果。
6. 预期错误返回 `ServiceResult`，没有异常穿透 UI。
7. 没有在 UI 线程执行文件、数据库或重型媒体操作。
8. 没有在 ViewModel 或 View 中直接调用播放器操作。
9. 没有在 VLC 原回调中访问 WPF。
10. 退出流程没有资源泄漏和重复释放。
11. 日志包含关键失败，但不包含逐帧或每次 Position 日志。
12. 测试证据和手工验收记录已保存。

任一条不满足时，不得开始下一个 Phase。

---

## 10. 冲突处理与变更流程

实现期间如果原 V1.0 与本补充规范不一致，按以下顺序处理：

1. 先停止当前 Phase 的功能扩展。
2. 记录冲突位置、原文、实现影响和建议。
3. 按以下优先级判断：
   - 本补充规范
   - 原 V1.0 产品范围
   - 当前 Phase 的最小实现
4. 如果冲突涉及新增产品功能，不得自行实现，必须回到需求确认。
5. 如果冲突只涉及实现细节，选择更简单、可测试、可回滚的方案。
6. 变更后重新运行当前 Phase 和相关回归测试。

任何对接口、状态机、线程模型、数据库结构或验收阈值的修改，都必须同步更新本文件和对应的测试。

---

## 11. 最终执行摘要

Codex 实现 VideoPlayer 时必须遵守以下不可变边界：

```text
一个生产 WPF 项目
一个测试项目
LibVLCSharp 负责解码和播放
PlayerService 唯一管理 MediaPlayer
PlayerViewModel 唯一负责编排
View 只绑定，不控制播放
VLC 回调不碰 WPF
所有播放命令经过 command gate
所有事件携带 MediaSessionId
过期会话事件必须丢弃
数据库和配置失败不影响播放
每次 Phase 必须 Build + Test + 量化验收
```
