# AI 开发交接说明
本文档供接手 `NPS.NetSpeed` 的 AI 开发工具阅读。它记录当前代码结构、宿主耦合点，以及本插件的构建约束与任务流程。
## Git 工作流与提交规范
所有新功能、Bug 修复和文档变更都遵循下面的流程，不要直接在 `main` 分支上修改。
### 开始任务前
1. 先检查工作区和当前分支：`git status --short --branch`。不要覆盖、丢弃或重置用户已有的未提交改动；如果这些改动会影响本任务，应先说明并等待处理方式。
2. 更新基础分支：切换到 `main` 后执行 `git pull --ff-only origin main`。禁止用强制重置覆盖远端或本地改动；如果同步发生冲突，先停止并处理冲突。
3. 从最新的 `main` 创建任务分支，再开始编辑：
   - 新功能：`<prefix>/feat-简短名称`
   - Bug 修复：`<prefix>/fix-简短名称`
   - 文档或维护：`<prefix>/docs-简短名称` 或 `<prefix>/chore-简短名称`
   - `<prefix>` 由团队或所使用的开发工具约定，例如团队缩写、用户名或工具名；不要把某一种工具名称写死为项目唯一前缀。
4. 分支名使用简短、稳定、可读的描述。后续操作都在任务分支上完成，不要直接提交到 `main`。
### 修改过程中
- 先阅读相关源码、宿主 API 文档和现有约定，再确定方案；保持改动范围与任务一致，不顺手重构无关代码。
- AI 设计方案、分析记录、临时补丁、截图和调试输出统一放在仓库根目录的 `.ai-work/` 下。这些文件是本地缓存，不属于交付内容，不要 `git add` 或提交。
- 其他工具产生的无关产物也不得进入仓库，包括 `.trae-html-share-packages/`、`.superpowers/`、`.uploads/`、HTML 预览包、截图、日志、PID/状态文件、个人配置、IDE 文件、构建输出和缓存；发现这类内容时应删除或移出工作区，不要提交。
- 只有明确属于项目交付物的源码、文档、资源和经过确认的发布产物才加入暂存区。提交前逐项检查 `git status` 和暂存区，避免使用无法确认内容的批量暂存命令。
- 不要擅自修改或删除用户已有改动；不确定文件是否应纳入任务时，先保留并询问。
### 完成任务前
在提交前完成以下检查：
1. 查看 `git diff`、`git diff --stat` 和 `git status --short`，确认差异只包含本任务内容。
2. 执行 `git diff --check`，修复空白错误；按项目约束进行源码审阅。不要在用户本机执行 `dotnet build`、`dotnet test` 或其他本地编译流程。
3. 再次确认 `.ai-work/`、截图、日志、构建目录、个人配置和敏感信息没有进入暂存区。不要使用无法逐项确认内容的 `git add -A`。
4. 功能或修复完成且检查通过后再提交。一个提交应表达一个完整目的，避免把未完成方案或无关格式化混入提交。
### 提交信息与后续操作
- 提交信息使用清晰的约定式格式：`<type>(<scope>): <简短摘要>`，例如 `feat(widget): 并列模式改为数值优先排版`、`docs(readme): 补充未编译说明`。
- `type` 优先使用 `feat`、`fix`、`docs`、`refactor`、`test`、`chore`；摘要说明实际结果，避免使用“更新代码”“一些修改”等无信息描述。必要时在提交正文说明原因、行为变化和验证方式。
- 提交后记录分支名、提交 ID、验证结果和未提交文件状态。除非用户明确要求，不要自动推送、合并、删除分支或改写已有提交。
## 开发约束
- 运行环境为用户本机时，**不要在用户本机执行 `dotnet build`、`dotnet test` 或其他本地编译流程，也不要还原 NuGet 包或安装 SDK**；应以源码审阅为主，并说明无法在本机验证构建。
- 当前运行环境若是用户明确授权的远程构建平台，则应在该环境中自行检查并下载所需的 .NET SDK、宿主源码和 NuGet 依赖，完成可行的构建与验证；不得把“禁止用户本机编译”误解为“禁止远程构建”。
- 仓库当前没有 CI 构建工作流或测试工程；`releases/` 目录中的预编译 DLL 对应最近一次远程构建，不代表每次源码改动都已通过构建或测试。远程构建需要生成或更新 DLL 时，仅将经过确认的发布产物放入 `releases/`，不要提交 `bin/`、`obj/` 或临时构建缓存。
- 改动宿主接口前，先查阅宿主对应版本的官方 API 文档和源码。不要仅凭其他 NotchPeninsula 版本的接口猜测。
- 尽量保持插件仍以单个 `NPS.NetSpeed.dll` 分发；不要把宿主运行时已提供的依赖（如 SkiaSharp）复制进插件目录。
## 项目与宿主
- 插件 ID：`com.snow.netspeed`
- 当前插件版本：`1.1.2`
- 目标框架：`net10.0-windows10.0.19041.0`
- 插件入口：`NetSpeedPlugin`
- 宿主 API 命名空间：`NotchPeninsula.Plugins`
- 目标宿主版本：**NotchPeninsula 正式版 2.1.0**（GitHub tag `NPS-v2.1.0`，release「NotchPeninsula 2.1.0」，2026-10-24 发布，非预发布）。本插件使用该版本已有的 `IAutoWidthWidget` / `WidgetKit`、`IPluginHost.ScheduleRefresh`、`GetSetting` / `SetSetting`、`InvalidateWidgetLayout`、`PostReminder`。以下结论均按该 tag 的源码核对，不依赖 `main` 分支：
  - `Plugin/PluginApi.cs`：`INotchPlugin`、`IWidget`、`IAutoWidthWidget`、`IDetailPage`、`WidgetFrame`、`WidgetHit`
  - `Plugin/WidgetKit.cs`：`WidgetKit.Row/Icon/Gap/Text/Stack/Min/ContentRect/Typeface`、`TextWeight`
  - `Plugin/PluginLoader.cs`：`plugins/<目录>/plugin.json` 的入口 DLL 约定
  - `Plugin/PluginHost.cs`：注册时回调 `OnActivate`，设置键按 `Plugin.<Id>.<key>` 隔离
  - `NotchPeninsula.csproj`：目标框架与 SkiaSharp 2.88.8
官方参考：
- [NotchPeninsula 仓库](https://github.com/GEORGEWWWU/NotchPeninsula)
- [NPS-v2.1.0 tag 的 NPS_PluginsAPI.md 插件开发说明](https://github.com/GEORGEWWWU/NotchPeninsula/blob/NPS-v2.1.0/NPS_PluginsAPI.md)
- [参考实现 NPS.AudioVisualizer](https://github.com/SnowDreamXUE/NPS.AudioVisualizer)
- 若目标版本有对应 tag，应优先使用该 tag 的 API 文档和宿主源码，不要默认 `main` 与已发布版本完全一致。
## 当前实现结构
仓库布局如下。`.csproj` 保留在仓库根目录以维持 `..\NotchPeninsula` 相对引用；源码按职责放在 `src/` 子目录，命名空间按目录拆分为 `NPS.NetSpeed` / `.Core` / `.Rendering` / `.UI`，跨命名空间引用需要显式 `using`：
```text
NPS.NetSpeed/
├── NPS.NetSpeed.csproj
├── plugin.json                       # 目录型插件入口清单
├── src/
│   ├── NetSpeedPlugin.cs
│   ├── Core/{NetSpeedState, NetworkSampler, SpeedFormat, HuorongLauncher}.cs
│   ├── Rendering/{SpeedPaint, PluginFont}.cs
│   └── UI/{NetSpeedWidget, NetSpeedDetailPage}.cs
├── releases/NPS.NetSpeed.dll         # 预编译分发产物
└── README.md / AI_DEVELOPMENT.md / .gitignore
```
### `src/NetSpeedPlugin.cs`
实现 `INotchPlugin` 和 `IDisposable`。`Initialize` 读取持久化设置（模式 / 采样间隔 / 统计网卡）、注册岛组件、按所选间隔用 `ScheduleRefresh` 启动采样，并发送一次提示提醒。`Tick` 只在后台线程推进 `NetSpeedState`，并在 nic 键从未落盘时自动选中 WLAN / Wi-Fi / 无线网卡（一次成功选择即落盘，用户手动切换或已显式选择则跳过）；不请求重绘（宿主按帧持续绘制）。`SetSampleInterval` / `CycleNic` 落盘设置、应用到采样器并 Dispose 旧句柄后按新间隔重调度定时器；`SetMode` 写入 `mode`，并调用 `InvalidateWidgetLayout()` + `RequestRedraw()` 让宿主按新模式重新测宽。`Dispose` 释放刷新句柄与采样器。
### `src/Core/NetworkSampler.cs`
纯 BCL 的网卡累计字节采样器。每 3 秒刷新网卡列表，只保留 `OperationalStatus.Up`、非 `Loopback` / `Tunnel` 且有 TCP/IP 单播地址的候选网卡（无任何单播地址的底层接口如 WAN Miniport，`ipconfig /all` 也不会显示，一并排除；属性查询失败的网卡按无绑定处理）；按当前采样间隔汇总参与统计网卡（全部候选或 `SetNicFilter` 指定的单张）`GetIPStatistics()` 的 IPv4 + IPv6 合计字节，用滑动窗口计算速率：快档为 1 秒窗口（至少 400ms 跨度），慢档窗口约 1.2 × 间隔，速率等于最近一个间隔的增量 ÷ 间隔。同时维护候选网卡 `NicInfo(Id, Name)` 快照供详情页切换。计数器回绕、网卡重连或参与统计的网卡集合变化时丢弃旧窗口并重设基线，避免异常大的瞬时值。`SetInterval` / `SetNicFilter` / `GetNicList` / `TryGetNicName` 供 UI 线程调用，内部用锁与定时器线程互斥。
### `src/Core/NetSpeedState.cs`
跨线程共享的上行 / 下行状态与平滑。采样线程 `Push` / `PushIdle`，绘制线程 `Read`。上升、下降分别使用不同系数，零流量再乘衰减，保证回落不突兀；同时保留 60 个样本供详情页折线使用。`Mode` 用 `Volatile` 读写。
### `src/Core/SpeedFormat.cs`
数值与单位拆分：最小单位由 `Format` 的 `minUnitIndex` 参数决定——岛上收起态组件为 `KB/s`（`MinUnitIndex`），详情页大号读数为 `B/s`（`LowestUnitIndex`），向上自适应 `KB/s` → `MB/s` → `GB/s`；单位切换带 15% 回差，避免读数在单位边界抖动；小数位按量级收敛（`B/s` 无小数，`KB/s` 小于 100 保留 1 位，`MB/s` / `GB/s` 小于 10 保留 2 位）。内部采样值低于 4 B/s 统一显示为零读数（岛上 `0 KB/s`，详情页 `0 B/s`）。
### `src/Rendering/SpeedPaint.cs`
主题派生配色与绘制工具。强调色由品牌色向 `RenderTheme.TextColor` 混合，深浅主题都保留对比度；矢量箭头不依赖字体字形；详情页折线只画线，不做面积填充。
### `src/Rendering/PluginFont.cs`
从宿主 `FontConfig` 取得字体。宿主字体属于共享资源：每次绘制按需取得，不缓存，也不要对其调用 `Dispose`。
### `src/UI/NetSpeedWidget.cs`
实现 `IAutoWidthWidget`，负责岛上的收起态组件。只画「箭头 + 数字 + 单位」，**没有任何条形、进度或面积填充图形**。`Layout` 用固定宽度 Box 描述排版，数值变化不会重测宽度；只有切换模式时才通知宿主重新布局。并列模式两个通道左右并排，双行模式上下各占一行。左键命中返回 `toggle-mode` 并切换模式，右键保持宿主默认行为。
### `src/UI/NetSpeedDetailPage.cs`
实现 `IDetailPage`，固定 360×262 设计坐标系，绘制时整体缩放并居中到当前 `rect`。包含标题行（标题居左、当前档位说明居右）、采样间隔五档芯片（动作 `interval:0..4`，独占标题下方一行，避免与标题重叠）、「并列 / 双行」模式胶囊、网卡切换行（动作 `nic:next`，显示当前统计网卡并在全部 / 各候选网卡间循环）、火绒流量监控行（动作 `open:netflow`，调用 `HuorongLauncher` 启动 `NetFlow.exe`，回退主界面 `wsctrl.exe`，均未检测到时由插件 `PostReminder` 提示）、下行 / 上行大号读数与最近 60 个样本折线；命中测试把 `rect` 相对坐标反算回设计坐标后判断。

### `src/Core/HuorongLauncher.cs`
静态工具类，定位并启动火绒流量监控。探测顺序：注册表配置键 `HKLM\SOFTWARE\Huorong\Sysdiag`（含 WOW6432Node）→ 卸载表 DisplayName 含「火绒 / Huorong」条目的 InstallLocation → `HipsDaemon` 服务 ImagePath → 默认 ProgramFiles 路径；候选根目录下先找 `bin\NetFlow.exe`（官方确认可直接吊起流量监控窗口），再找 `bin\wsctrl.exe` 作主界面回退。只在按钮点击时执行（注册表读取微秒级，无需缓存）；启动用 `Process.Start(UseShellExecute = true)`，失败或未检测到返回 false 由插件提示。
## 数据采集与绘制数据流
1. 采样完全由插件负责：`NetworkInterface.GetAllNetworkInterfaces()` + `GetIPStatistics()`，不依赖宿主未公开的数据源，也不读取 `WidgetFrame.Bars`。
2. `ScheduleRefresh` 的回调运行在宿主后台线程，最小间隔 100ms 且不叠加；切换采样间隔档位时 Dispose 旧句柄并按新间隔重新调度。回调内只更新共享状态，不做 IO 以外的绘制工作。
3. 宿主按渲染帧调用 `Layout` / `Draw`；`Draw` 直接读当帧快照并格式化文本，不保存宿主传入的 `WidgetFrame` 或其数组。
4. 切换模式会改变组件宽度：`SetMode` 必须同时调用 `InvalidateWidgetLayout()` 与 `RequestRedraw()`。
5. `HitTest` / `OnLeftClick` 的 `x`、`y` 是相对组件 `rect` 的局部坐标；详情页命中则需映射回设计坐标。
## 设置键和值
设置由宿主 `GetSetting` / `SetSetting` 保存，宿主会按插件 ID 隔离设置键（注册表路径 `HKCU\SOFTWARE\NotchPeninsula`，键名前缀 `Plugin.com.snow.netspeed.`）。
| 键 | 索引含义 | 当前缺省值 |
| --- | --- | --- |
| `mode` | `0` 并列；`1` 双行 | `0` |
| `interval` | `0` 100ms；`1` 500ms；`2` 1秒；`3` 5秒；`4` 10秒 | `0` |
| `nic` | 统计网卡 Id（Windows 下为 GUID）；空串 = 全部候选网卡。首次运行自动写入检测到的 WLAN 网卡 Id | 无（自动检测） |

`ReadSettings` 会把非法值回落到默认。本插件**不使用** `RegisterSettingsPage`：在 `NPS-v2.1.0` 中该接口存在，但本次核对未在插件中心 / 设置界面找到消费 `SettingsPages` 的入口；模式切换、采样间隔与网卡选择均由组件左键与详情页交互完成。若未来宿主接通设置页，再补充 `ISettingsPage` 实现，并保持各键的索引含义不变。
## 渲染资源和线程约定
- `SKPaint`、`SKPath` 等 Skia 绘制资源在绘制方法内创建并及时释放；不要跨帧缓存这些对象。
- 不要释放宿主借用的 `SKTypeface`；AutoWidth 文字使用 `WidgetKit.Typeface(TextWeight)`。
- 保持详情页固定设计坐标系的布局方式，避免展开动画尺寸变化导致控件跳动。
- 采样线程与 UI 绘制线程分离：绘制路径不等待锁、不做阻塞 IO；共享状态只在 `NetSpeedState` 内同步。
- 任何改变组件宽度的改动，都要确认宿主收到重新测量通知；仅改变数值文本不需要通知。
## 项目引用与依赖
`NPS.NetSpeed.csproj` 通过相对路径引用 `..\NotchPeninsula\NotchPeninsula.csproj`，并设置 `Private="false"`。SkiaSharp 版本为 2.88.8，配置为 `ExcludeAssets="runtime"`，运行时依赖由宿主提供。以上框架与包版本已核对与宿主 2.1.0（tag `NPS-v2.1.0`）一致。
如果远程环境需要构建，两个仓库必须按相对路径并列放置，不能把插件源码放进宿主仓库目录（宿主项目的默认源文件通配可能把插件代码一并编译进宿主）：
```text
工作目录/
├── NotchPeninsula/       # 与目标版本匹配的宿主源码（tag NPS-v2.1.0）
└── NPS.NetSpeed/         # 本插件
```
在授权的远程环境中，先确认或安装 .NET 10 SDK，并按上述布局检出宿主源码与 NuGet 依赖，然后进入 `NPS.NetSpeed` 目录构建。项目的 `InstallToHostPlugins` 目标会把 DLL 与 `plugin.json` 复制到宿主输出目录的 `plugins\NPS.NetSpeed\`。本插件已在授权远程构建环境（.NET SDK 10.0.403、Linux）按下列命令完成编译，结果为 0 警告 0 错误：
```powershell
dotnet build NPS.NetSpeed.csproj -c Release -p:EnableWindowsTargeting=true
```
Windows 构建产物位于 `bin\Release\net10.0-windows10.0.19041.0\NPS.NetSpeed.dll`。构建成功并确认无误后，将产物更新到 `releases/NPS.NetSpeed.dll`，作为对应源码版本的分发产物。
Linux/macOS 环境构建需追加 Windows RID 与目标框架开关（宿主引用 WindowsDesktop 框架，缺 `-r win-x64` 会报 NETSDK1082）：
```bash
dotnet build NPS.NetSpeed.csproj -c Release -r win-x64 -p:EnableWindowsTargeting=true
```
此时产物位于 `bin/Release/net10.0-windows10.0.19041.0/win-x64/NPS.NetSpeed.dll`。
