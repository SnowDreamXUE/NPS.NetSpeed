# NPS.NetSpeed
NotchPeninsula 的网络上行 / 下行速度插件，在灵动岛组件和详情页中显示实时网速。项目以 C# 编写，当前版本为 **1.1.0**，插件 ID 为 `com.snow.netspeed`。

> **预编译产物：`releases/` 目录提供与本仓库源码对应的 `NPS.NetSpeed.dll`**（在授权远程构建环境按宿主 tag `NPS-v2.1.0` 实际编译，0 警告 0 错误）。尚未在宿主中实际运行验证；如自行构建，请按本文的构建说明执行。
## 功能
- 实时显示下行与上行速度，单位自适应 `B/s`、`KB/s`、`MB/s`、`GB/s`，小数位按量级收敛。
- 采样间隔五档可调：**100ms / 500ms / 1秒 / 5秒 / 10秒**，在详情页芯片切换并持久化；速率窗口随间隔缩放，慢档速率为「最近一个间隔内的字节增量 ÷ 间隔」。
- 统计网卡可选择：详情页网卡行点击在「全部网卡」与单个候选网卡之间循环切换并持久化；首次运行自动默认选中 WLAN / Wi-Fi 网卡（与火绒等安全软件的默认监控口径对齐，未检测到无线网卡时保持全部），便于对数排查口径差异；网卡插拔 / VPN 启停后候选列表自动刷新。
- 两种显示模式，均可通过左键点击组件或详情页按钮切换：**并列**（两个通道左右并排）与**双行**（上下行各占一行）。
- 数值优先的简洁排版：每个通道只画「箭头 + 数字 + 单位」，箭头取强调色、数值取主文字色、单位取次要文字色，不含任何条形填充或进度语义图形。
- 跟随宿主深浅主题：强调色由品牌色与 `RenderTheme.TextColor` 混合派生，黑字与白字主题都保留对比度。
- 零流量不突跳：采样值经过指数平滑，读数在 `4 B/s` 以下统一显示 `0 B/s`；单位切换带 15% 回差，避免在单位边界反复跳动。
- 详情页（右键展开）显示大号读数、最近 60 个样本折线（跨度随采样间隔变化）与采样间隔 / 模式 / 网卡选择；折线只画线，不做面积填充。
## 数据采集与显示
数据完全由插件采集，不依赖宿主未公开的接口：`System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()` 枚举已启用、非环回、非隧道的候选网卡（或用户在详情页选定的单张网卡），按当前采样间隔（默认 100ms，详情页五档可调）汇总参与统计网卡 `GetIPStatistics()` 的 IPv4 + IPv6 合计字节，再用滑动窗口计算 `B/s`（快档为 1 秒窗口、至少 400ms 跨度；慢档窗口约 1.2 × 间隔）。采样通过宿主 `IPluginHost.ScheduleRefresh` 驱动，切换档位时 Dispose 旧句柄并按新间隔重新调度，回调运行在后台线程，只更新共享状态；宿主按渲染帧调用组件的 `Layout` / `Draw`，组件直接从共享状态读取当帧读数。

网卡列表每 3 秒刷新一次。计数器回绕、网卡重连、统计网卡集合变化（插拔、VPN 启停、切换网卡选择）时会丢弃旧窗口并重设基线，避免把新网卡「从开机起累计」的字节误算成一次巨大的瞬时速度。

灵动岛上的网速组件可通过宿主显示设置启用和调整位置。点击组件可在并列与双行之间切换；详情页顶部同样提供两个模式胶囊。设置保存在宿主提供的插件设置中，重启后保留。
## 安装
安装需要一份与目标 NotchPeninsula 版本兼容的预编译 `NPS.NetSpeed.dll`，可直接从本仓库 `releases/` 目录获取；仓库同时提供全部源代码。
1. 从本仓库 `releases/` 目录取得 `NPS.NetSpeed.dll`，或按后文「源码构建」自行生成。
2. 将 `NPS.NetSpeed.dll` 与 `plugin.json` 放入宿主 exe 同级的 `plugins\NPS.NetSpeed\`（例如 `C:\NotchPeninsula\plugins\NPS.NetSpeed\`），或在宿主的 **设置 → 我的插件** 中导入 DLL，然后按宿主提示重启或重新加载插件。
3. 在 **设置 → 显示设置 → 显示内容** 中启用并调整组件显示。

### 源码构建
1. 克隆宿主仓库并切到发布 tag：
   ```powershell
   git clone https://github.com/GEORGEWWWU/NotchPeninsula.git
   cd NotchPeninsula
   git checkout NPS-v2.1.0
   ```
2. 将本仓库与宿主源码按相对路径并列放置：
   ```text
   工作目录/
   ├── NotchPeninsula/       # 宿主源码（tag NPS-v2.1.0）
   └── NPS.NetSpeed/         # 本插件
   ```
3. 在具备 .NET 10 SDK 的环境中构建（不要在未授权的环境里直接执行）：
   ```powershell
   dotnet build NPS.NetSpeed.csproj -c Release -p:EnableWindowsTargeting=true
   ```
   预期产物为 `bin\Release\net10.0-windows10.0.19041.0\NPS.NetSpeed.dll`。
4. 确认产物无误后，将其更新到本仓库 `releases\NPS.NetSpeed.dll`，作为对应源码版本的分发产物。

构建成功后，项目里的 `InstallToHostPlugins` 目标会自动把 DLL 与 `plugin.json` 复制到宿主输出目录的 `plugins\NPS.NetSpeed\`。如遇编译差异，请以 `NPS-v2.1.0` 的实际源码为准并在此补充记录。
## 项目结构
| 文件 | 职责 |
| --- | --- |
| `src/NetSpeedPlugin.cs` | 插件入口、设置读取、采样生命周期与模式切换 |
| `src/Core/NetSpeedState.cs` | 跨线程共享的速度状态、平滑与历史样本 |
| `src/Core/NetworkSampler.cs` | 网卡枚举、累计字节采样与滑动窗口速率计算 |
| `src/Core/SpeedFormat.cs` | 数值与单位拆分、回差与小数位规则 |
| `src/Rendering/SpeedPaint.cs` | 主题派生配色、矢量箭头与详情页折线 |
| `src/Rendering/PluginFont.cs` | 从宿主取得当前字体 |
| `src/UI/NetSpeedWidget.cs` | 灵动岛上的网速组件及其 AutoWidth 布局、左键切换模式 |
| `src/UI/NetSpeedDetailPage.cs` | 详情页读数、最近 60 个样本折线与采样间隔 / 模式 / 网卡选择、火绒入口交互 |
| `NPS.NetSpeed.csproj` | 目标框架、宿主项目引用和编译依赖 |
| `plugin.json` | 目录型插件的入口清单，指定 `NPS.NetSpeed.dll` |
| `releases/NPS.NetSpeed.dll` | 与当前版本对应的预编译产物 |
| `AI_DEVELOPMENT.md` | 供 AI 开发工具接手时阅读的项目约定和构建说明 |

参考实现中的 `VisualizerSettingsPage.cs` 在本插件中没有对应文件：`NPS-v2.1.0` 虽然保留了 `RegisterSettingsPage` 接口，但本次核对未在插件中心 / 设置界面找到消费 `SettingsPages` 的入口，模式切换改由组件左键与详情页完成。
## 开发环境说明
项目当前面向 `net10.0-windows10.0.19041.0`。项目文件位于仓库根目录，通过相对路径引用同级的 `NotchPeninsula` 宿主源码工程，因此单独检出本仓库并不能解析完整的编译引用。当前仓库没有 CI 构建工作流和测试工程，预编译产物位于 `releases/` 目录；构建所需的目录布局、依赖与注意事项见 [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md)。

本插件当前适配的宿主为 **NotchPeninsula 正式版 2.1.0**（GitHub release [NotchPeninsula 2.1.0](https://github.com/GEORGEWWWU/NotchPeninsula/releases/tag/NPS-v2.1.0)，对应 tag `NPS-v2.1.0`，2026-10-24 发布，非预发布）。接口结论均按该 tag 的 `Plugin/PluginApi.cs`、`Plugin/WidgetKit.cs`、`Plugin/PluginLoader.cs`、`Plugin/PluginHost.cs` 与 `NotchPeninsula.csproj` 核对；更新宿主兼容性前，请以对应 tag 的源码和插件 API 文档为准，不要默认 `main` 分支与已发布版本一致：
- [NotchPeninsula 宿主仓库](https://github.com/GEORGEWWWU/NotchPeninsula)
- [NPS-v2.1.0 的插件开发 API 文档](https://github.com/GEORGEWWWU/NotchPeninsula/blob/NPS-v2.1.0/NPS_PluginsAPI.md)
- [参考实现 NPS.AudioVisualizer](https://github.com/SnowDreamXUE/NPS.AudioVisualizer)

已知未验证点：
- 已完成编译但未在宿主中实际运行验证：组件行内的实际像素高度由宿主决定，本插件按传入 `rect` 自适应排版。
- `GetIPStatistics()` 的统计口径包含以普通网卡形式出现的 VPN 流量；`Tunnel` 类型网卡已排除。
- 无线网卡驱动在休眠唤醒后重置计数器时，会丢掉一个采样窗口，表现为一帧 `0 B/s`。

## 版本记录
- **1.1.0**：详情页新增「火绒流量监控」一键直达按钮（点击探测并启动火绒 `NetFlow.exe`，未找到时回退火绒主界面 `wsctrl.exe`，均未检测到则弹出提示）；采样间隔新增 500ms 档共五档（100ms / 500ms / 1秒 / 5秒 / 10秒）；首次运行自动默认统计 WLAN 网卡；重排详情页布局，修复标题与采样芯片重叠。
- **1.0.0**：首个版本。对齐 NotchPeninsula 正式版 2.1.0（tag `NPS-v2.1.0`）；提供并列与双行两种数值优先的显示模式、单位自适应、零流量平滑回落与深浅主题适配；详情页支持采样间隔档位与统计网卡选择；包含 AI 开发交接说明。插件 ID 定为 `com.snow.netspeed`；已在授权远程构建环境完成编译（0 警告 0 错误），预编译 DLL 位于 `releases/` 目录。
## 许可
本项目基于 [MIT License](LICENSE) 开源，版权归 **SnowDreamXUE** 所有（2026 年）。宿主 NotchPeninsula 采用 Apache License 2.0；本插件未复制宿主源码，仅通过公开插件 API 交互，因此可以独立以 MIT 许可分发。
