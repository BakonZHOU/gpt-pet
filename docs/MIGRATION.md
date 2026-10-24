# 移植经验与后续开发

本文记录桌面版移植为 GPT/Codex 本地插件浮窗时的关键结论，以及以后修改、测试和发布的固定流程。当前基线为插件版 `v0.3.5`。

## 1. 最终架构

- 原始 DeepSeek 项目只作为参考，不在原目录修改。
- 插件采用官方本地 MCP 结构：根目录放 `plugin.json`、`mcp.json`，由 `stdio` 启动包内的 `native/CodexPetPlugin.exe`。
- 人物不是网页面板，也不是控制已安装桌面版的软件。插件自带一份独立 WPF 浮窗；启用插件后，GPT 初始化 MCP 服务时启动它。
- 插件和旧桌面版使用不同的数据目录、互斥锁及通信管道，因此不会互相控制或覆盖设置。
- 不注册开机启动、不监视 GPT 启动、不写 Windows 卸载项。禁用插件或退出 GPT 后，插件浮窗自行关闭。

## 2. 移植中最重要的经验

### 保持产品隔离

不要直接复用桌面版的安装目录、进程、注册表或启动快捷方式。插件数据固定放在：

```text
%LOCALAPPDATA%\CodexPetPlugin\<插件 ID 摘要>\
```

`PluginPaths.cs` 根据插件 ID 生成专属摘要，并派生设置文件、互斥锁和管道名称。迁移只读取本插件旧应用内版的 `preferences.json`、`quotes.json`，不读取桌面版数据。

### MCP 只负责生命周期，不承载控制面板

`PluginMcp.cs` 只提供：

- `pet_show`：显式恢复或显示浮窗；
- `pet_status`：只读检查插件浮窗是否运行。

`resources/list` 返回空数组，避免再出现网页控制面板。所有操作继续在人物上完成：右键人物打开原设置，左键按原三次点击逻辑显示额度、重置次数/概率语录、隐藏。

### 原生浮窗不能继承 MCP 管道

从 MCP 后端启动 WPF 浮窗时必须使用 `UseShellExecute = true`。仅重定向标准输入、输出、错误仍可能让 GUI 继承其他可继承句柄，导致 MCP 连接关闭后宿主一直等待。

浮窗先复制到用户运行缓存，再从缓存启动。这样运行中的 WPF 文件不会锁住插件安装目录，禁用、升级或卸载插件时更稳定。

### 生命周期要以插件和 GPT 为准

`PluginWindowLifetime.cs` 每秒检查：

- 插件配置仍为启用；
- GPT 桌面进程仍存在。

连续两次确认 GPT 不存在后才关闭，避免瞬时检测误判。人物菜单里的手动“退出”会记录当前会话抑制标记，防止 MCP 的重复发现立即把它重开；重新启用插件、GPT 新会话、版本升级或调用 `pet_show` 可以恢复。

### 额度读取必须是元数据只读链路

`CodexClient.cs` 只允许以下方法：

```text
initialize
initialized
account/rateLimits/read
```

数据来源是本机 `codex app-server --listen stdio://` 的额度元数据。长连接接收 `account/rateLimits/updated`，再用 10 秒轮询和会话文件活动作为兜底。它不创建对话、不请求模型、不消耗 Token 或重置次数。

每累计下降 1% 单独提醒一次；首次读取、额度重置和重复快照不能误报。

### 视觉和交互应统一修改

- 皮肤资源：`assets/`；运行时资源由项目文件链接进 `native/Assets/`。
- 皮肤与主题色：`native-src/PetSkins.cs`。
- 人物、气泡、点击、右键菜单、呼吸和呆毛：`native-src/PetWindow.cs`。
- 配置默认值与保存：`native-src/PetSettings.cs`。
- 三套默认语料：`quotes/whale.txt`、`quotes/mutsumi.txt`、`quotes/dragon.txt`；用户副本分别为 `语录-鲸鱼娘.txt`、`语录-若叶睦.txt`、`语录-GPT娘白龙.txt`。切换皮肤时同步切换“编辑语录”的目标文件。
- 任务栏避让：`native-src/TaskbarAvoidance.cs`。
- 额度解析和 5 小时/周窗口：`native-src/Quota.cs`、`QuotaDropTracker.cs`。

新增皮肤时，人物 PNG、眼睛风格、呆毛、气泡主题色和菜单选项要一次完成，不能只换图片。皮肤 PNG 保持透明背景和与现有素材一致的画布、锚点、边距，否则窗口缩放和任务栏避让会错位。

## 3. 后续修改流程

1. 只在 `plugin` 分支修改，不回写 DeepSeek 原项目，也不改已发布的历史 tag。
2. 修改源码或资源；若增加文件，同步检查 `.csproj` 的打包规则。
3. 同时更新版本号：
   - `plugin.json`；
   - `native-src/CodexPetPlugin.csproj`；
   - `native-src/PluginMcp.cs` 的 `serverInfo.version`；
   - `.agents/plugins/marketplace.json` 的制品目录；
   - README 中的版本和制品路径。
4. 完整构建，不能在原生二进制已变化时使用旧的 `-ReuseBuild`：

```powershell
./build.ps1
```

5. 运行核心检查：

```powershell
dotnet run --project native-tests/CodexPetPlugin.Tests.csproj -c Release
./tests/mcp-smoke.ps1
./tests/ui-smoke.ps1
```

6. GPT 已运行时，再做隔离生命周期测试：

```powershell
node tests/lifecycle.cjs
```

该测试使用临时 `CODEX_HOME` 和测试插件 ID，不应读写用户真实插件配置。

7. 生成发布包：

```powershell
./package.ps1 -ReuseBuild
```

确认 ZIP 根目录直接包含 `plugin.json`，且包内没有 Node、网页控制面板、安装脚本、开机启动项或开发缓存。

8. 安装新版本验证：启用后浮窗自动出现；右键设置、三次点击、声音、三套皮肤、每 1% 提醒、小窗口定位、任务栏避让、禁用退出和重新启用均需检查。
9. 提交并推送 `plugin` 分支；等待 GitHub Actions 全绿后再建立新 release，上传 ZIP 和 `.sha256`。旧 release 可标为历史版本，但不要移动或重写已经发布的 tag。

## 4. 给别人安装

当前尚未进入 OpenAI 通用公开插件目录，因此用官方支持的 Git 市场分发。对方在 Windows 版 GPT/Codex 的终端环境执行：

```powershell
codex plugin marketplace add BakonZHOU/gpt-pet --ref plugin
codex plugin add codex-pet-app@bakon-gpt-pet
```

之后重开 GPT。第二条也可以改为在 GPT 的“插件目录”中选择 `Bakon GPT Pet` 来源，再安装并启用“额度伙伴 · 插件浮窗”。

为保证 Git 市场能直接安装，`.agents/plugins/marketplace.json` 指向的完整插件制品必须提交进 `plugin` 分支，不能只存在于被 `.gitignore` 忽略的本地 `artifacts` 目录。单文件构建启用压缩，使自带 .NET 运行时的原生程序保持在 GitHub 单文件上限以内。

## 5. 常见问题

- **启用插件却没有立即出现**：宿主可能尚未重新加载插件目录；刷新插件或重开 GPT。插件不能用随包 Hook 绕过宿主加载机制。
- **重启 GPT 后未立刻出现**：先打开任意本地聊天，使宿主初始化已启用的 MCP。插件代码只有在宿主加载后才能运行；若要求在 GPT 进程出现的瞬间启动，就必须使用独立监视程序。不要为规避这一限制加入 `SessionStart` 命令 Hook，否则需要额外信任确认，而且含 Hook 的包目前不符合公开插件目录提交条件。
- **升级后还是旧窗口**：检查 `window.pid` 指向的文件版本。MCP 会关闭低版本窗口并启动新版本，但不会让旧 MCP 连接降级一个更高版本的窗口。
- **同一人物出现两份**：先区分插件浮窗和独立桌面版。插件实例由专属互斥锁限制为一个；桌面版是另一套产品。
- **关闭 MCP 后进程不退出**：检查 GUI 是否错误继承了 MCP 句柄，启动方式必须保持 `UseShellExecute = true`。
- **额度只在点击时更新**：检查推送连接、10 秒轮询和会话活动监听是否都在工作，不要通过模型请求来“刷新”。
- **小窗口或任务栏处错位**：检查 DPI、工作区边界、素材透明边距和 `TaskbarAvoidance`，不要只用全屏坐标验证。

## 6. 发布边界

本仓库提供的是符合本地 MCP 插件结构的 Windows x64 包。进入 OpenAI 官方公开插件目录仍需要相应审核、身份验证或合作伙伴资格；GitHub release 不等同于已经在官方商店上架。
