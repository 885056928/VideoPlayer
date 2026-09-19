# VideoPlayer V1.0 Codex 开发规范书

> 文档版本：V1.0
> 项目名称：VideoPlayer
> 项目类型：Windows 本地视频播放器
> 技术栈：C# + WPF + MVVM + LibVLCSharp
> 开发方式：Codex 分阶段实现
> 项目结构：单 Solution + 单 WPF Project

---

# 1. 项目概述

## 1.1 项目名称

项目名称统一为：

**VideoPlayer**

解决方案：

```text
VideoPlayer.sln
```

项目：

```text
VideoPlayer
```

默认命名空间：

```text
VideoPlayer
```

---

## 1.2 项目定位

VideoPlayer 是一个运行于 Windows 平台的本地视频播放器。

主要用于：

* 播放电脑本地视频文件
* 播放本地视频文件夹
* 管理当前播放列表
* 字幕加载与切换
* 音轨切换
* 记忆播放进度
* 视频截图
* 查看视频信息
* 全屏播放

V1.0 的核心目标不是功能数量最大化，而是：

> **播放稳定、启动快速、操作简单、界面简洁、代码结构清晰。**

---

# 2. V1.0 功能范围

## 2.1 必须实现

V1.0 必须包含以下功能。

### 视频播放

支持：

* 打开本地视频
* 播放
* 暂停
* 停止
* 上一个视频
* 下一个视频
* 自动播放下一个
* 拖动进度条
* 快进
* 快退

---

### 视频格式

默认支持：

```text
.mp4
.mkv
.avi
.mov
.wmv
.ts
.m2ts
.webm
```

实际视频编码格式由 LibVLC/VLC 能力决定。

不得自行实现视频解码器。

---

### 播放控制

支持：

* 播放/暂停
* 停止
* 进度控制
* 音量控制
* 静音
* 播放速度
* 快进
* 快退

播放速度：

```text
0.5x
0.75x
1.0x
1.25x
1.5x
1.75x
2.0x
```

同时允许通过快捷键调整播放速度。

---

# 3. 播放列表

## 3.1 基础功能

支持：

* 添加视频
* 删除视频
* 清空播放列表
* 双击播放
* 上一个
* 下一个
* 自动播放下一项
* 拖拽视频加入播放列表
* 打开文件夹并扫描视频

---

## 3.2 播放列表数据

播放列表项目至少包含：

```csharp
public class PlaylistItem
{
    public string FilePath { get; set; }

    public string FileName { get; set; }

    public TimeSpan? Duration { get; set; }

    public bool IsPlaying { get; set; }
}
```

---

## 3.3 文件夹播放

用户选择文件夹后：

1. 扫描目录
2. 查找支持的视频文件
3. 加入播放列表
4. 按文件名排序
5. 自动播放第一个视频

默认只扫描当前目录。

是否递归扫描子目录可以作为配置项。

---

# 4. 拖拽操作

主窗口必须支持：

```text
视频文件 → 播放器
视频文件 → 播放列表
文件夹 → 播放器
文件夹 → 播放列表
```

拖入视频：

```text
加入播放列表
并播放
```

拖入多个视频：

```text
全部加入播放列表
播放第一个
```

---

# 5. 字幕功能

## 5.1 支持格式

V1.0 支持：

```text
.srt
.ass
.ssa
```

---

## 5.2 自动加载字幕

如果：

```text
Movie.mkv
Movie.srt
```

文件位于同一目录，并且文件名匹配，则自动尝试加载字幕。

例如：

```text
Movie.mkv
Movie.srt
```

---

## 5.3 手动加载字幕

支持：

```text
打开字幕文件
```

用户可以选择：

```text
.srt
.ass
.ssa
```

---

## 5.4 字幕切换

如果一个视频存在多个字幕轨道，需要提供字幕轨道选择。

至少支持：

```text
关闭字幕
字幕 1
字幕 2
字幕 3
...
```

---

# 6. 音轨

支持读取 LibVLC 提供的音频轨道。

例如：

```text
中文
English
Japanese
...
```

用户可以在播放过程中切换音轨。

---

# 7. 全屏

支持：

```text
F11
```

进入/退出全屏。

也支持：

```text
Esc
```

退出全屏。

全屏时：

* 隐藏窗口边框
* 视频区域最大化
* 播放控制栏正常使用
* 鼠标移动到窗口底部时显示控制栏

---

# 8. 播放历史

VideoPlayer 必须记录播放历史。

## 8.1 数据

至少包含：

```text
Id
FilePath
FileName
Duration
LastPosition
LastPlayedAt
PlayCount
```

---

## 8.2 断点续播

用户播放：

```text
Movie.mkv
```

退出程序后再次打开：

如果存在历史记录：

```text
Movie.mkv
```

则询问：

```text
继续播放
从头播放
```

如果用户选择继续：

```text
从 LastPosition 开始播放
```

---

## 8.3 播放完成

如果视频已经播放到接近结尾：

```text
LastPosition >= Duration * 0.95
```

可以认为已经播放完成。

再次播放时默认：

```text
从头开始
```

---

# 9. 截图

支持：

```text
Ctrl + Alt + S
```

截图当前视频画面。

默认保存位置：

```text
Pictures/
└── VideoPlayer/
    └── yyyy-MM-dd/
```

例如：

```text
Pictures/
└── VideoPlayer/
    └── 2026-09-20/
        └── Movie_00-12-35.png
```

截图文件名至少包含：

```text
视频名称
播放时间
```

---

# 10. 视频信息

提供视频信息窗口。

至少显示：

```text
文件名
文件路径
文件大小
视频时长
视频分辨率
视频编码
视频帧率
音频编码
音频采样率
音频声道
```

信息来源优先使用 LibVLC 提供的数据。

不得自行实现复杂媒体解析器。

---

# 11. 快捷键

V1.0 默认快捷键：

| 快捷键              | 功能        |
| ---------------- | --------- |
| Space            | 播放/暂停     |
| Left             | 后退 5 秒    |
| Right            | 前进 5 秒    |
| Shift + Left     | 后退 30 秒   |
| Shift + Right    | 前进 30 秒   |
| Up               | 音量 +5     |
| Down             | 音量 -5     |
| M                | 静音        |
| [                | 播放速度降低    |
| ]                | 播放速度提高    |
| \                | 恢复 1.0x   |
| F11              | 全屏        |
| Esc              | 退出全屏      |
| Ctrl + O         | 打开文件      |
| Ctrl + Shift + O | 打开文件夹     |
| Ctrl + P         | 显示/隐藏播放列表 |
| Ctrl + I         | 视频信息      |
| Ctrl + Alt + S   | 截图        |

快捷键不得与文本输入控件产生明显冲突。

---

# 12. UI 设计

## 12.1 整体布局

采用简洁播放器布局：

```text
┌──────────────────────────────────────────────┐
│ VideoPlayer                         ─ □ ×    │
├──────────────────────────────────────────────┤
│                                              │
│                                              │
│                 VIDEO AREA                   │
│                                              │
│                                              │
├──────────────────────────────────────────────┤
│ 00:12:35 ━━━━━━━━━━━━━━━ 01:35:20           │
│                                              │
│ ▶  ⏮  ⏭   🔊 ━━━━━  1.0x   ⚙   ⛶           │
├──────────────────────────────────────────────┤
│ Playlist                                     │
│ ├─ Movie1.mkv                                │
│ ├─ Movie2.mp4                                │
│ └─ Movie3.avi                                │
└──────────────────────────────────────────────┘
```

---

## 12.2 UI 原则

V1.0：

* 简洁
* 深色主题
* 播放区域优先
* 控件数量不要过多
* 不做复杂动画
* 不做复杂皮肤系统
* 不引入过重 UI 框架

---

# 13. 技术架构

采用：

```text
View
  ↓
ViewModel
  ↓
Service
  ↓
LibVLCSharp
  ↓
VLC
```

禁止：

```text
View
  ↓
直接调用 LibVLC
```

---

# 14. 项目结构

整个解决方案只能包含：

```text
1 个 Solution
1 个 WPF Project
```

禁止创建：

```text
VideoPlayer.Core
VideoPlayer.Infrastructure
VideoPlayer.Services
VideoPlayer.UI
VideoPlayer.Common
```

等 Class Library。

---

## 14.1 最终目录

```text
VideoPlayer/
│
├── VideoPlayer.sln
│
└── VideoPlayer/
    │
    ├── App.xaml
    ├── App.xaml.cs
    │
    ├── MainWindow.xaml
    ├── MainWindow.xaml.cs
    │
    ├── Views/
    │   ├── PlayerView.xaml
    │   ├── PlaylistView.xaml
    │   ├── VideoInfoView.xaml
    │   └── SettingsView.xaml
    │
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   ├── PlayerViewModel.cs
    │   ├── PlaylistViewModel.cs
    │   ├── VideoInfoViewModel.cs
    │   └── SettingsViewModel.cs
    │
    ├── Models/
    │   ├── PlaylistItem.cs
    │   ├── PlaybackHistory.cs
    │   ├── VideoInfo.cs
    │   └── AppSettings.cs
    │
    ├── Services/
    │   ├── PlayerService.cs
    │   ├── PlaylistService.cs
    │   ├── HistoryService.cs
    │   ├── SubtitleService.cs
    │   ├── MediaInfoService.cs
    │   ├── ScreenshotService.cs
    │   ├── FileService.cs
    │   └── SettingsService.cs
    │
    ├── Interfaces/
    │   ├── IPlayerService.cs
    │   ├── IPlaylistService.cs
    │   ├── IHistoryService.cs
    │   └── ISettingsService.cs
    │
    ├── Database/
    │   ├── DatabaseContext.cs
    │   └── DatabaseInitializer.cs
    │
    ├── Configuration/
    │   └── AppConfiguration.cs
    │
    ├── Enums/
    │   ├── PlaybackState.cs
    │   └── PlayerTrackType.cs
    │
    ├── Controls/
    │   ├── VideoControlBar.xaml
    │   └── VideoProgressBar.xaml
    │
    ├── Converters/
    │
    ├── Resources/
    │   ├── Styles/
    │   ├── Templates/
    │   └── Icons/
    │
    └── Helpers/
        ├── FileHelper.cs
        ├── TimeHelper.cs
        └── DispatcherHelper.cs
```

---

# 15. MVVM 规范

使用：

```text
CommunityToolkit.Mvvm
```

ViewModel 推荐继承：

```csharp
ObservableObject
```

Command 使用：

```csharp
RelayCommand
AsyncRelayCommand
```

禁止在 ViewModel 中直接创建：

```csharp
new LibVLC()
new MediaPlayer()
```

这些对象必须由 PlayerService 管理。

---

# 16. PlayerService

PlayerService 是播放器核心。

接口：

```csharp
public interface IPlayerService
{
    PlaybackState State { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    int Volume { get; set; }

    double Rate { get; set; }

    bool IsMuted { get; }

    void Open(string filePath);

    void Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);

    void SetMute(bool mute);
}
```

实际实现可以根据 LibVLCSharp API 进行调整。

---

# 17. PlaybackState

定义：

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

播放器状态必须保持明确。

禁止使用大量：

```csharp
bool IsPlaying
bool IsPaused
bool IsStopped
bool IsOpening
bool IsEnded
```

互相组合表达状态。

优先使用：

```csharp
PlaybackState
```

---

# 18. Service 设计

## 18.1 PlayerService

负责：

```text
LibVLC
MediaPlayer
播放
暂停
停止
Seek
音量
静音
播放速度
音轨
字幕轨
播放器状态
```

---

## 18.2 PlaylistService

负责：

```text
播放列表
添加
删除
排序
清空
下一项
上一项
文件夹扫描
自动播放下一项
```

---

## 18.3 HistoryService

负责：

```text
播放历史
播放进度
播放次数
最后播放时间
断点续播
```

---

## 18.4 SubtitleService

负责：

```text
字幕自动搜索
字幕加载
字幕切换
字幕关闭
```

---

## 18.5 MediaInfoService

负责：

```text
视频信息
音频信息
文件信息
```

---

## 18.6 ScreenshotService

负责：

```text
截图
截图路径生成
文件命名
```

---

## 18.7 FileService

负责：

```text
文件扫描
目录扫描
视频文件过滤
路径处理
```

---

## 18.8 SettingsService

负责：

```text
配置加载
配置保存
默认配置
配置迁移
```

---

# 19. 数据库

V1.0 使用：

```text
SQLite
```

ORM 可以使用：

```text
Entity Framework Core SQLite
```

但不要为了数据库引入复杂的数据访问层。

---

## 19.1 History 表

```text
PlaybackHistory
```

字段：

```text
Id
FilePath
FileName
Duration
LastPosition
LastPlayedAt
PlayCount
```

---

## 19.2 数据库位置

建议：

```text
%AppData%/VideoPlayer/
```

例如：

```text
%AppData%/VideoPlayer/VideoPlayer.db
```

---

# 20. 配置文件

配置文件：

```text
%AppData%/VideoPlayer/appsettings.json
```

示例：

```json
{
  "Player": {
    "DefaultVolume": 80,
    "DefaultPlaybackRate": 1.0,
    "AutoPlayNext": true,
    "RememberPosition": true
  },
  "Subtitle": {
    "AutoLoad": true,
    "Encoding": "UTF-8"
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

---

# 21. 第三方依赖

核心依赖：

```text
LibVLCSharp.WPF
VideoLAN.LibVLC.Windows
CommunityToolkit.Mvvm
Microsoft.Extensions.DependencyInjection
Microsoft.EntityFrameworkCore.Sqlite
Serilog
Serilog.Sinks.File
```

Codex 在安装依赖时必须：

1. 检查当前稳定版本
2. 确认 API 与当前版本匹配
3. 不使用已经废弃的旧 API
4. 不凭记忆猜测第三方库 API

---

# 22. LibVLC 初始化

程序启动时初始化 LibVLC。

原则：

```text
Application Start
        ↓
初始化 LibVLC
        ↓
创建 PlayerService
        ↓
创建 ViewModel
        ↓
创建 MainWindow
```

应用退出时：

```text
停止播放
↓
释放 MediaPlayer
↓
释放 Media
↓
释放 LibVLC
↓
保存必要状态
↓
退出
```

必须避免：

```text
资源泄漏
重复初始化
重复释放
```

---

# 23. DI

使用：

```text
Microsoft.Extensions.DependencyInjection
```

在：

```text
App.xaml.cs
```

中建立：

```csharp
ServiceCollection
```

注册：

```text
PlayerService
PlaylistService
HistoryService
SubtitleService
MediaInfoService
ScreenshotService
FileService
SettingsService
```

ViewModel 通过构造函数注入 Service。

例如：

```csharp
public PlayerViewModel(
    IPlayerService playerService,
    IPlaylistService playlistService)
{
    _playerService = playerService;
    _playlistService = playlistService;
}
```

---

# 24. UI 线程规范

这是 VideoPlayer 的重要要求。

禁止在 UI Thread 执行：

```text
大目录扫描
数据库查询
数据库写入
文件批量操作
截图文件保存
大量媒体信息解析
```

必须使用：

```text
async/await
Task
后台线程
```

进行处理。

---

# 25. 播放进度刷新

播放进度不需要每一帧刷新 UI。

建议：

```text
100ms ~ 250ms
```

刷新一次 UI。

例如：

```text
播放器实际播放
       ↓
Timer
       ↓
读取 Position
       ↓
更新 ViewModel
       ↓
更新 ProgressBar
```

禁止：

```text
每一帧触发 WPF PropertyChanged
```

---

# 26. 性能要求

VideoPlayer 必须优先保证视频播放性能。

要求：

* 播放时 UI 不明显卡顿
* Seek 不应阻塞界面
* 大视频文件不能导致 UI 卡死
* 文件扫描不能阻塞 UI
* 数据库操作不能阻塞 UI
* 播放进度更新不能过于频繁
* 播放列表需要考虑大量文件情况

如果播放列表超过：

```text
1000
```

项，应避免一次性进行大量复杂 UI 操作。

---

# 27. 硬件加速

V1.0 使用：

```text
LibVLC / VLC
```

提供的硬件加速能力。

不要自行实现：

```text
DXVA2
D3D11VA
NVDEC
QuickSync
AMF
```

等硬件解码层。

除非后续实际发现性能问题，再针对特定平台进行优化。

---

# 28. 中文路径兼容

必须正确处理：

```text
C:\视频\电影\测试.mp4
```

以及：

```text
C:\My Videos\Movie 01.mkv
```

包含：

* 中文
* 空格
* 特殊字符
* Unicode
* 长路径

不得通过简单字符串拼接破坏文件路径。

优先使用：

```csharp
System.IO.Path
```

相关 API。

---

# 29. 日志

使用：

```text
Serilog
```

日志位置：

```text
%AppData%/VideoPlayer/Logs/
```

格式：

```text
yyyy-MM-dd.log
```

---

## 29.1 需要记录

记录：

```text
程序启动
程序退出
视频打开
视频打开失败
视频播放错误
文件不存在
字幕加载失败
数据库异常
截图失败
VLC 异常
未处理异常
```

---

## 29.2 禁止高频日志

禁止记录：

```text
每一帧
每次 Position 更新
每次鼠标移动
```

避免日志影响性能。

---

# 30. 异常处理

必须处理：

```text
文件不存在
文件无权限
视频格式无法解析
VLC 初始化失败
MediaPlayer 初始化失败
字幕加载失败
数据库异常
截图失败
配置文件损坏
```

原则：

```text
记录详细日志
UI 给出用户可理解的错误提示
程序尽可能继续运行
```

禁止因为一个视频无法播放直接导致整个程序退出。

---

# 31. 配置文件损坏

如果：

```text
appsettings.json
```

损坏：

1. 记录错误
2. 使用默认配置
3. 程序继续启动
4. 必要时重新生成配置

---

# 32. 数据库损坏

如果 SQLite 数据库无法读取：

1. 记录错误
2. 不影响播放器启动
3. 尝试创建新的数据库
4. 播放功能必须可以继续使用

数据库不能成为播放器启动的硬依赖。

---

# 33. 启动流程

完整启动流程：

```text
程序启动
    ↓
初始化日志
    ↓
加载配置
    ↓
初始化数据库
    ↓
初始化 DI
    ↓
初始化 LibVLC
    ↓
创建 ViewModel
    ↓
创建 MainWindow
    ↓
显示窗口
```

任何非核心模块失败：

```text
记录日志
继续启动
```

---

# 34. 退出流程

程序退出：

```text
停止播放
    ↓
保存播放状态
    ↓
保存配置
    ↓
释放 MediaPlayer
    ↓
释放 Media
    ↓
释放 LibVLC
    ↓
关闭数据库
    ↓
关闭日志
```

---

# 35. V1.0 不实现的功能

以下功能明确排除在 V1.0：

```text
在线视频
YouTube
网络视频
网络流媒体
NAS
DLNA
UPnP
视频下载
视频编辑
视频剪辑
视频转码
视频压制
AI 字幕
AI 翻译
AI 插帧
弹幕
云同步
账号系统
手机 App
插件市场
在线视频搜索
在线字幕搜索
在线视频库
```

这些功能以后如果需要，再单独规划 V2.0。

---

# 36. 开发阶段

Codex 必须按照阶段开发。

禁止一次性生成整个项目。

---

## Phase 1：项目初始化

完成：

```text
创建 Solution
创建 WPF Project
安装依赖
创建目录
配置 DI
配置 Serilog
创建 MainWindow
创建 MainViewModel
创建 PlayerView
创建 PlayerViewModel
创建 IPlayerService
创建 PlayerService
```

实现最小播放功能：

```text
打开视频
播放
暂停
停止
```

完成后：

```bash
dotnet build
```

并启动程序确认可以运行。

---

## Phase 2：播放器核心

实现：

```text
Seek
Position
Duration
Volume
Mute
PlaybackRate
PlaybackState
```

实现：

```text
进度条
音量条
播放速度
```

---

## Phase 3：播放列表

实现：

```text
添加文件
删除
清空
上一首
下一首
自动播放下一首
文件夹播放
拖拽
```

---

## Phase 4：字幕和音轨

实现：

```text
自动字幕
手动字幕
字幕切换
关闭字幕
音轨切换
```

---

## Phase 5：播放历史

实现：

```text
SQLite
History
播放次数
最后播放时间
播放位置
断点续播
```

---

## Phase 6：高级播放器功能

实现：

```text
全屏
快捷键
截图
视频信息
```

---

## Phase 7：UI 优化

优化：

```text
深色主题
控件样式
动画
播放控制栏
播放列表
错误提示
空状态
加载状态
```

注意：

> UI 优化不能影响播放稳定性。

---

## Phase 8：测试

测试：

```text
MP4
MKV
AVI
MOV
TS
M2TS
WebM
```

测试：

```text
中文路径
空格路径
特殊字符路径
超大文件
超长视频
大量播放列表
```

测试：

```text
播放
暂停
Seek
快进
快退
音量
静音
倍速
字幕
音轨
截图
全屏
断点续播
```

---

# 37. Codex 开发规则

Codex 必须遵守以下规则。

## 规则 1：单项目

整个解决方案只能：

```text
VideoPlayer.sln
    └── VideoPlayer
```

禁止拆分多个 Class Library。

---

## 规则 2：不要过度设计

V1.0 不要引入：

```text
CQRS
MediatR
AutoMapper
Repository Pattern
UnitOfWork
EventBus
微服务
```

除非实际需求出现，否则不使用。

---

## 规则 3：View 不直接操作播放器

错误：

```csharp
private MediaPlayer _player;
```

在 View 中直接操作。

正确：

```text
View
 ↓
ViewModel
 ↓
IPlayerService
 ↓
PlayerService
 ↓
LibVLC
```

---

## 规则 4：Service 单一职责

不要创建：

```text
GodService
```

例如不要让：

```text
PlayerService
```

同时负责：

```text
数据库
截图
字幕搜索
配置
文件扫描
```

---

## 规则 5：优先使用现有库能力

对于：

```text
播放
Seek
音频
字幕
媒体信息
截图
```

优先使用 LibVLC 能力。

不要重复实现 VLC 已经提供的功能。

---

## 规则 6：先检查 API

使用第三方库时：

```text
先检查当前项目实际安装版本
↓
查看当前 API
↓
再编写代码
```

禁止凭旧版本记忆生成代码。

---

## 规则 7：每个阶段都必须 Build

每个 Phase 完成后必须：

```bash
dotnet build
```

如果出现错误：

```text
分析
修复
重新 Build
```

直到当前 Phase 可以正常编译。

---

## 规则 8：不要提前实现后续功能

例如当前任务是：

```text
Phase 2
播放器核心
```

不要顺便实现：

```text
字幕
数据库
截图
视频信息
```

避免一次修改范围过大。

---

# 38. 第一阶段 Codex 指令

第一阶段只允许完成以下内容：

```text
1. 创建 VideoPlayer.sln

2. 创建 VideoPlayer WPF Project

3. 配置项目目标框架

4. 安装必要 NuGet 包

5. 创建项目目录

6. 配置 DI

7. 配置 Serilog

8. 创建 MainWindow

9. 创建 MainViewModel

10. 创建 PlayerView

11. 创建 PlayerViewModel

12. 创建 IPlayerService

13. 创建 PlayerService

14. 初始化 LibVLC

15. 实现打开本地视频

16. 实现播放

17. 实现暂停

18. 实现停止

19. 将 LibVLC 视频画面显示到 WPF

20. 执行 dotnet build

21. 修复编译错误

22. 启动程序确认播放器可以正常打开

23. 完成后停止，不要继续开发 Phase 2
```

---

# 39. 第一阶段完成标准

必须满足：

```text
VideoPlayer 可以启动
```

能够：

```text
打开 MP4
```

能够：

```text
播放
```

能够：

```text
暂停
```

能够：

```text
停止
```

视频画面正常显示。

程序关闭后：

```text
没有明显异常
```

执行：

```bash
dotnet build
```

结果：

```text
Build succeeded.
```

---

# 40. 最终验收标准

VideoPlayer V1.0 最终必须满足：

### 播放

```text
✓ 本地视频播放
✓ 播放/暂停
✓ 停止
✓ Seek
✓ 快进/快退
✓ 音量
✓ 静音
✓ 倍速
```

### 播放列表

```text
✓ 添加
✓ 删除
✓ 清空
✓ 上一个
✓ 下一个
✓ 文件夹播放
✓ 拖拽
✓ 自动播放下一项
```

### 字幕

```text
✓ SRT
✓ ASS
✓ SSA
✓ 自动加载
✓ 手动加载
✓ 字幕切换
```

### 音频

```text
✓ 音轨读取
✓ 音轨切换
```

### 播放状态

```text
✓ 历史记录
✓ 断点续播
✓ 播放次数
```

### 高级功能

```text
✓ 全屏
✓ 快捷键
✓ 截图
✓ 视频信息
```

### 稳定性

```text
✓ 中文路径
✓ 空格路径
✓ 特殊字符路径
✓ 大文件
✓ 长时间播放
✓ 播放异常恢复
✓ 数据库异常不影响播放
```

---

# 41. Codex 总体执行要求

Codex 在整个 VideoPlayer 项目开发过程中必须遵守：

```text
先规划
↓
分阶段实现
↓
每阶段编译
↓
发现问题立即修复
↓
确认当前阶段稳定
↓
再进入下一阶段
```

不得：

```text
一次性生成整个项目
```

不得：

```text
为了实现功能而随意增加项目层级
```

不得：

```text
为了“架构漂亮”引入不必要的设计模式
```

不得：

```text
修改已经正常工作的模块而没有明确需求
```

不得：

```text
在 UI Thread 执行耗时任务
```

不得：

```text
View 直接操作 LibVLC
```

不得：

```text
凭旧版本 API 编写 LibVLCSharp 代码
```

---

# 42. 项目最终架构

最终整体结构保持：

```text
                    ┌─────────────────────┐
                    │        WPF View     │
                    │                     │
                    │ MainWindow           │
                    │ PlayerView           │
                    │ PlaylistView         │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │      ViewModel      │
                    │                     │
                    │ MainViewModel        │
                    │ PlayerViewModel      │
                    │ PlaylistViewModel    │
                    └──────────┬──────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │       Services      │
                    │                     │
                    │ PlayerService        │
                    │ PlaylistService      │
                    │ HistoryService       │
                    │ SubtitleService      │
                    │ MediaInfoService     │
                    │ ScreenshotService    │
                    │ FileService          │
                    │ SettingsService      │
                    └──────────┬──────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
        ┌───────────┐    ┌───────────┐    ┌───────────┐
        │ LibVLC    │    │  SQLite   │    │   Files   │
        │ / VLC     │    │ Database  │    │ / Config  │
        └───────────┘    └───────────┘    └───────────┘
```

---

# 43. V1.0 核心原则

VideoPlayer V1.0 最重要的不是“功能最多”，而是：

> **简单、稳定、快速、易维护。**

开发优先级：

```text
播放稳定性
    >
性能
    >
核心功能完整性
    >
异常处理
    >
代码可维护性
    >
UI 美观
```

任何新增功能都不能破坏上述优先级。

---

# 44. 项目名称最终确认

以后所有：

```text
Solution
Project
Namespace
目录
配置文件
日志目录
数据库
截图目录
Codex 指令
开发文档
```

统一使用：

```text
VideoPlayer
```

最终基础结构：

```text
VideoPlayer.sln
└── VideoPlayer
```

命名空间：

```csharp
namespace VideoPlayer;
```

项目正式名称：

# VideoPlayer
