# VideoPlayer

<img src="VideoPlayer/Resources/Icons/VideoPlayer.png" width="96" alt="VideoPlayer 应用图标" />

基于 **WPF、.NET 10 和 LibVLCSharp** 的 Windows 本地视频播放器。采用 MVVM 结构，提供播放列表、断点续播、字幕加载、画面截图和快捷键操作。

源码仓库：[885056928/VideoPlayer](https://github.com/885056928/VideoPlayer)。

## 功能

- **本地播放**：通过文件选择器、文件夹扫描或拖放添加视频。文件筛选支持 MP4、MKV、AVI、MOV、WMV、TS、M2TS、WebM；实际解码能力取决于 LibVLC 和媒体编码。
- **播放控制**：播放、暂停、停止、快进快退、进度条点击与拖动定位、音量、静音及 0.25×～5.0× 倍速。
- **播放列表**：批量添加、移除、清空、上一项/下一项，以及播放结束后自动播放下一项；文件夹扫描按路径自然顺序排列。
- **断点续播**：使用 SQLite 保存播放历史和进度，重新打开符合续播条件的视频时自动恢复位置。
- **字幕**：自动发现同目录、同主文件名的 SRT、ASS、SSA 字幕，也可手动添加或关闭字幕。
- **画面与信息**：全屏播放、控制栏自动隐藏、进度悬停画面预览、PNG 截图及媒体信息查看。
- **窗口界面**：统一的蓝色播放图标用于程序文件、任务栏与窗口顶栏；深色自绘顶栏，支持拖动、双击最大化、边缘缩放，以及最小化、最大化 / 还原和关闭。
- **配置与恢复**：JSON 配置持久化、配置备份恢复、数据库损坏备份与重建，以及异常日志记录。

## 应用图标

应用图标采用深色圆角底板、蓝色环绕标记和白色播放三角，呼应播放器的深色界面。源图位于 `VideoPlayer/Resources/Icons/VideoPlayer.png`；同目录的 `VideoPlayer.ico` 包含 16、24、32、48、64、128、256 像素版本，用于 Windows 程序文件和窗口图标。资源随程序嵌入，无须单独复制图标文件。

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

## 使用方法

**打开视频**：点击底部工具栏的“打开视频”按钮、按 `Ctrl+O` 选择文件，或将视频文件、文件夹拖入窗口。点击顶栏文件夹按钮或按 `Ctrl+Shift+O` 可以选择文件夹并扫描其中的视频，包含子目录；双击播放列表项目可打开对应视频。图标按钮可通过悬停提示识别。

**定位与音量**：点击进度条目标位置或拖动圆点即可定位，播放中的视频继续播放，暂停中的视频保持暂停。移动到进度条上可查看对应时间和画面预览。音量条支持点击及拖动，静音按钮可切换静音状态；重新打开视频时恢复为非静音。

**播放倍率**：按 `[` 降速、`]` 加速，每次调节 0.25×，最低 0.25×、最高 5.0×；按 `\` 恢复正常速度 1.0×。工具栏右侧的倍率数字仅显示当前值。音量和倍率会保存到配置，并在后续播放时沿用。

**全屏与窗口**：点击顶栏“全屏”按钮或按 `F11` 进入全屏，按 `F11` 或 `Esc` 退出。全屏时隐藏顶栏和播放列表，退出后恢复原窗口位置、尺寸、最大化状态及播放列表显示状态。全屏控制栏闲置 2 秒后自动隐藏，移动鼠标或点击视频区域可重新显示。普通窗口下的 `Esc` 不会关闭播放器。

**列表、字幕与截图**：点击顶栏播放列表按钮或按 `Ctrl+P` 显示 / 隐藏列表。字幕按钮用于添加外部字幕或关闭当前字幕；按 `Ctrl+Alt+S` 保存画面截图，按 `Ctrl+I` 查看当前媒体信息。

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Space` | 播放 / 暂停 |
| `←` / `→` | 后退 / 前进 5 秒 |
| `Shift+←` / `Shift+→` | 后退 / 前进 30 秒 |
| `Ctrl+←` / `Ctrl+→` | 上一项 / 下一项 |
| `↑` / `↓` | 音量增加 / 减少 5 |
| `M` | 静音 / 取消静音 |
| `[` / `]` | 降低 / 提高播放速度，每次 0.25×，范围 0.25×～5.0× |
| `\` | 恢复 1.0× 速度 |
| `F11` | 切换全屏 |
| `Esc` | 退出全屏 |
| `Ctrl+O` | 打开视频文件 |
| `Ctrl+Shift+O` | 打开视频文件夹 |
| `Ctrl+P` | 显示 / 隐藏播放列表 |
| `Ctrl+I` | 查看媒体信息 |
| `Ctrl+Alt+S` | 保存截图 |

使用快捷键前让播放器窗口获得焦点。文本输入框获得焦点时，普通播放快捷键会让位于文本编辑；`F11` 和全屏下的 `Esc` 仍可使用。

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
| `Player.DefaultPlaybackRate` | `1.0` | 默认播放速度，范围 `0.25`～`5.0` |
| `Player.AutoPlayNext` | `true` | 播放结束后自动播放下一项 |
| `Player.RememberPosition` | `true` | 保存进度并启用断点续播 |
| `Subtitle.AutoLoad` | `true` | 自动加载同名外部字幕 |
| `UI.ShowPlaylist` | `false` | 是否显示播放列表；界面切换会自动保存 |
| `Screenshot.Directory` | 空字符串 | 截图目录；为空时使用系统“图片”目录下的 `VideoPlayer` |

启用 `RememberPosition` 且播放历史可用时，重新打开文件会自动恢复保存的位置，已标记播放完成的视频从头播放。保存进度达到时长的 95% 时会标记完成。正常播放期间会定期保存进度，暂停、停止、切换媒体及正常关闭时会尝试保存最终进度。

配置文件内容损坏时，应用优先读取有效备份并重建主配置；主配置和备份均无效时使用默认配置。读取或保存失败会记录诊断。数据库只有在确认损坏时才会备份重建；其他初始化失败会保留原数据库，并将历史功能降级。

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

测试覆盖播放状态、导航与结束事件竞态、历史循环和续播、配置并发保存与失败恢复、数据库初始化与恢复、字幕、截图及非关键异常诊断。界面回归还覆盖进度点击定位、进度与音量轨道绘制、自绘顶栏、全屏切换及状态恢复；倍率回归覆盖上下界、快捷键步长和配置保存重载。

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

## 常见问题

- **首次构建失败**：检查 Windows x64 环境及 .NET SDK 版本，确认 NuGet 源可访问，再执行锁定模式还原。项目使用 WPF，不能作为跨平台桌面应用在 Linux 或 macOS 上运行。
- **视频无法打开或播放**：确认文件存在且可读，查看 `%APPDATA%\VideoPlayer\Logs` 下的日志。文件扩展名进入扫描列表不代表其中的媒体编码一定能够解码。
- **找不到倍速选择入口**：使用 `[`、`]` 和 `\` 快捷键调整，工具栏中的倍率数字仅用于显示。
- **截图或预览没有生成**：检查目标目录是否可写，并查看日志。悬停预览失败时仍显示对应时间。

## 第三方许可证

依赖版本、许可及分发说明见 [LICENSES/README.md](LICENSES/README.md)。发布脚本会复制依赖提供的许可文件并生成依赖声明。

当前仓库未声明项目源码自身的许可证。
