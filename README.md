# VideoPlayer

基于 **WPF、.NET 10 和 LibVLCSharp** 的 Windows 本地视频播放器。采用 MVVM 结构，提供播放列表、断点续播、字幕加载、画面截图和快捷键操作。

## 功能

- **本地播放**：通过文件选择器、文件夹扫描或拖放添加视频。文件筛选支持 MP4、MKV、AVI、MOV、WMV、TS、M2TS、WebM；实际解码能力取决于 LibVLC 和媒体编码。
- **播放控制**：播放、暂停、停止、快进快退、进度条点击与拖动定位、音量、静音及 0.5×～2.0× 倍速。
- **播放列表**：批量添加、移除、清空、上一项/下一项，以及播放结束后自动播放下一项；文件夹扫描按文件名自然顺序排列。
- **断点续播**：使用 SQLite 保存播放历史和进度，重新打开符合续播条件的视频时自动恢复位置。
- **字幕**：自动发现同目录、同主文件名的 SRT、ASS、SSA 字幕，也可手动添加或关闭字幕。
- **画面与信息**：全屏播放、控制栏自动隐藏、进度悬停画面预览、PNG 截图及媒体信息查看。
- **配置与恢复**：JSON 配置持久化、配置备份恢复、数据库损坏备份与重建，以及异常日志记录。

## 环境要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows x64，支持 WPF 桌面应用 |
| 开发 SDK | .NET SDK 10.0.401，或 `global.json` 允许的同功能带更新补丁 |
| 目标框架 | `net10.0-windows` |
| 构建架构 | `win-x64` / x64 |
| 验收与发布脚本 | PowerShell 7（`pwsh`） |

播放引擎及原生库由 `LibVLCSharp.WPF` 和 `VideoLAN.LibVLC.Windows` NuGet 包提供。开发时需要还原 NuGet 依赖，无须另行安装 VLC 桌面客户端。

## 快速开始

在 Windows 的 PowerShell 中执行：

```powershell
git clone https://github.com/885056928/VideoPlayer.git
cd VideoPlayer

dotnet restore VideoPlayer.sln --locked-mode
dotnet build VideoPlayer.sln -c Release --no-restore
dotnet run --project VideoPlayer/VideoPlayer.csproj -c Release --no-build --no-restore
```

也可以用支持 .NET 10 和 WPF 的 IDE 打开 `VideoPlayer.sln`，将 `VideoPlayer` 设为启动项目。

启动后点击“打开视频”、按 `Ctrl+O` 选择文件，或将视频拖入窗口。按 `Ctrl+Shift+O` 可以选择文件夹并扫描其中的视频，包含子目录；双击播放列表项目可打开对应视频。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Space` | 播放 / 暂停 |
| `←` / `→` | 后退 / 前进 5 秒 |
| `Shift+←` / `Shift+→` | 后退 / 前进 30 秒 |
| `Ctrl+←` / `Ctrl+→` | 上一项 / 下一项 |
| `↑` / `↓` | 音量增加 / 减少 5 |
| `M` | 静音 / 取消静音 |
| `[` / `]` | 降低 / 提高播放速度 |
| `\` | 恢复 1.0× 速度 |
| `F11` | 切换全屏 |
| `Esc` | 退出全屏 |
| `Ctrl+O` | 打开视频文件 |
| `Ctrl+Shift+O` | 打开视频文件夹 |
| `Ctrl+P` | 显示 / 隐藏播放列表 |
| `Ctrl+I` | 查看媒体信息 |
| `Ctrl+Alt+S` | 保存截图 |

## 配置与数据

应用数据默认保存在 `%APPDATA%\VideoPlayer`，与源码和程序安装目录分开：

| 位置 | 用途 |
| --- | --- |
| `appsettings.json` | 当前配置 |
| `appsettings.json.bak` | 配置备份 |
| `VideoPlayer.db` | SQLite 播放历史数据库 |
| `Logs/` | 按日滚动的应用日志 |

截图默认保存在系统“图片”目录下的 `VideoPlayer` 文件夹；悬停预览缓存位于 `%TEMP%\VideoPlayer\Previews`。

如需手动调整配置，请先退出播放器，再编辑 `appsettings.json` 中对应的字段：

| 配置字段 | 默认值 | 说明 |
| --- | --- | --- |
| `Player.DefaultVolume` | `80` | 默认音量 |
| `Player.DefaultPlaybackRate` | `1.0` | 默认播放速度 |
| `Player.AutoPlayNext` | `true` | 播放结束后自动播放下一项 |
| `Player.RememberPosition` | `true` | 保存进度并启用断点续播 |
| `Subtitle.AutoLoad` | `true` | 自动加载同名外部字幕 |
| `Screenshot.Directory` | 空字符串 | 截图目录；为空时使用系统“图片”目录下的 `VideoPlayer` |

续播会跳过已经播放完成、时长不足 30 秒、已播放不足 15 秒，以及接近结尾的视频。正常播放期间会定期保存进度，暂停、停止、切换媒体及正常关闭时会尝试保存最终进度。

当前配置无法读取时，应用会尝试备份，再回退到默认配置，并记录诊断。数据库只有在确认损坏时才会备份重建；其他初始化失败会保留原数据库，并将历史功能降级。

## 项目结构

```text
VideoPlayer.sln
VideoPlayer/                 WPF 应用
  MainWindow.xaml            主窗口与布局
  Views/                     播放、播放列表、媒体信息及全屏视图
  ViewModels/                界面状态与命令
  Services/                  播放、文件扫描、历史、配置、字幕与截图服务
  Interfaces/                服务契约
  Models/                    播放快照、会话、配置与业务结果
  Database/                  EF Core 上下文、迁移与数据库初始化
  Configuration/             应用数据路径
  Resources/Styles/          界面颜色与控件样式
  CompositionRoot.cs         依赖注入注册
VideoPlayer.Tests/           单元测试与集成测试
scripts/acceptance/          构建、覆盖率、架构、发布与长期运行检查
docs/                       规范、设计及人工验收文档
LICENSES/                   第三方依赖许可证说明
global.json                 SDK 版本约束
```

业务和播放交互主要位于 ViewModel 与 Service；纯视图行为保留在视图层。数据库访问使用 EF Core SQLite，日志使用 Serilog，属性与命令使用 CommunityToolkit.Mvvm。依赖版本锁定于项目文件和 `packages.lock.json`。

## 测试与验收

先完成“快速开始”中的还原和 Release 构建，然后执行：

```powershell
dotnet test VideoPlayer.sln -c Release --no-build --no-restore
pwsh -NoProfile -File scripts/acceptance/Test-Architecture.ps1
```

测试覆盖播放状态、导航与结束事件竞态、历史循环和续播、配置并发保存与失败恢复、数据库初始化与恢复、字幕、截图及非关键异常诊断等行为。

完整自动验收入口会执行还原、构建、测试、覆盖率及架构检查，并将结果写入 `artifacts/acceptance/`：

```powershell
pwsh -NoProfile -File scripts/acceptance/Invoke-Acceptance.ps1
```

真实媒体格式、画面显示、全屏和长时间播放需另外执行人工或运行验收，参见 [人工验收矩阵](docs/acceptance/manual-matrix.md) 与 `scripts/acceptance/Start-SoakTest.ps1`。矩阵中的 `Pending` 表示尚未执行，不能视为通过。

## 发布

生成 Windows x64 自包含文件夹：

```powershell
pwsh -NoProfile -File scripts/acceptance/Publish-Release.ps1
```

输出为 `artifacts/publish/win-x64/`，该目录会在发布时重新生成。运行其中的 `VideoPlayer.exe` 即可启动；目标机器无须单独安装 .NET 运行时。分发时应保留整个输出目录，包含 LibVLC 原生库、插件和 `LICENSES/`，不能只复制 EXE。

源码仓库忽略 `bin/`、`obj/`、IDE 缓存、测试结果及 `artifacts/`。可运行的发布文件夹由上述脚本单独生成。

## 第三方许可证

依赖版本、许可及分发说明见 [LICENSES/README.md](LICENSES/README.md)。发布脚本会复制依赖提供的许可文件并生成依赖声明。

当前仓库未声明项目源码自身的许可证。
