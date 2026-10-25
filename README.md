# 额度伙伴 · 插件浮窗 v0.4.0

长按人物：初始按压动画结束再持续 1 秒进入蓄力，用鼠标调整红色箭头方向，松开发射。持续按住 15 秒满蓄力；飞行带重力、旋转和最多 8 次衰减反弹，落地停留 2 秒后自动回正并回到右下角，发射后的完整流程少于 8 秒。

Windows x64 与 macOS 13+ 本地MCP插件，自带桌宠运行程序，无需软件安装器。GPT加载启用插件的MCP服务时启动桌面浮窗；不需要打开“额度伙伴”面板。

安装后首次启用会请求信任插件的本地启动钩子。信任后，GPT建立本地会话时会立即拉起桌宠；若进程异常退出，下一次发送消息会补做一次轻量状态确认并恢复。禁用插件或退出GPT仍会自动关闭，不安装独立常驻监视器。

## 操作

右键人物打开原版设置。点击依次显示额度、重置次数或概率语录、隐藏气泡。三套皮肤为欧鲸鲸、若叶睦、龙娘；配套主题气泡、原版音效、呼吸和呆毛0.1–6秒、每1%提醒、拖动与任务栏或Dock避让保持原样。按下音效在松开、拖动或失去捕获时立即停止，连续点击不会叠音。隐藏和退出仍在人物右键菜单内。

每套皮肤使用独立语料文件：欧鲸鲸为 DeepSeek 梗、龙娘为 GPT/AI 梗、若叶睦为 MyGO/Ave Mujica 梗。切换皮肤后“编辑语录”会打开对应文件。5小时额度为0%时，第二次点击固定显示当前语料中以“区”开头的句子。

## 独立运行与退出

- 使用插件专属设置目录、单实例互斥锁与通信管道，不读取或控制已安装桌面软件。两者可以同时运行。
- 禁用插件或退出GPT后，插件浮窗自动关闭。多个GPT连接只启动一个插件浮窗；短暂MCP连接结束不会关闭它。
- 手动退出后，重复工具发现不会立即重开；重新启用插件或使用插件的“显示插件桌宠”工具可恢复。
- 启动时会把运行文件放入用户运行缓存，避免浮窗占用插件安装目录而妨碍卸载。不写Windows安装注册表，不创建开机启动项。
- 数据与运行缓存位于 `%LOCALAPPDATA%/CodexPetPlugin/<插件标识摘要>/`。仅首次迁移本插件旧应用内版的设置和语录，桌面版数据不变。

纯插件不能在 GPT 尚未加载插件系统时提前运行。GPT 重启后，打开任意本地聊天让宿主初始化已启用的 MCP，浮窗会自动出现；若刚切换启用状态仍未出现，重开 GPT 或调用“显示插件桌宠”。要做到 GPT 进程一出现就无条件启动，只能另装监视程序，本版本刻意不这样做。

## 额度

仅调用Codex只读额度元数据接口，长连接推送、文件活动合并刷新与10秒轮询兜底。不创建聊天、不调用模型、不消耗重置机会。

## 安装与发布

官方本地MCP结构：根目录plugin.json、mcp.json和包内原生stdio程序。正式目录上架仍需要OpenAI合作伙伴白名单及审核，本GitHub包不代表已进入官方商店。

### 发给别人安装

Windows 用户执行：

```powershell
codex plugin marketplace add BakonZHOU/gpt-pet --ref plugin
codex plugin add codex-pet-app@bakon-gpt-pet
```

macOS 用户执行：

```bash
codex plugin marketplace add BakonZHOU/gpt-pet --ref plugin
codex plugin add codex-pet-app-macos@bakon-gpt-pet
```

然后重开 GPT。也可以只执行第一条命令，重开 GPT 后在“插件目录”选择 `Bakon GPT Pet` 来源，安装并启用“额度伙伴 · 插件浮窗”。这属于官方支持的 Git 市场安装方式，不需要桌面软件安装器。更新时执行 `codex plugin marketplace upgrade bakon-gpt-pet`，再在插件目录更新；如果宿主尚未重载，重开 GPT。

开发者在Windows x64运行 `./build.ps1`；macOS运行 `./build-macos.sh`。两个平台均为0.4.0，`package.ps1` 与 `package-macos.sh` 生成各自ZIP，plugin.json直接位于ZIP根目录。

发布版可以作为 Codex Git 市场安装：`codex plugin marketplace add BakonZHOU/gpt-pet --ref plugin`，再安装 `codex-pet-app@local-whale-pet`。桌宠右键的「检查更新」会先用 Codex 原生命令刷新市场并比较版本；相同版本不重复安装，发现新版则在 GPT 关闭、文件锁释放后自动调用原生安装命令。

无自动更新的 Git 市场旧版，退出 GPT 后重新执行 `codex plugin marketplace upgrade local-whale-pet`，再执行 `codex plugin add codex-pet-app@local-whale-pet`。最早的本地开发市场版需先执行 `codex plugin marketplace remove local-whale-pet`，再按上面的 Git 市场安装命令重新添加；桌宠设置和语录会保留。

启用开关保存加载许可，启动动作发生在GPT初始化插件服务时；客户端若尚未重新加载插件目录，需刷新插件或重开GPT。公开目录不支持通过随包Hook绕过宿主加载机制。

## 验证

只读额度、每1%通知、三次点击、皮肤、音效资源、任务栏避让及小窗口行为沿用原生实现。另有独立实例、重复初始化、连接断开、禁用退出、重新启用和手动退出等生命周期检查。当前GPT的设置开关点击本身仍需用户在宿主中验证，不能把stdio检查描述为完整客户端UI验收。

来源：
- https://developers.openai.com/plugins/build/plugins
- https://developers.openai.com/plugins/deploy/desktop-only

开发者可在已运行GPT的Windows本机执行 "node tests/lifecycle.cjs"，在隔离的临时配置中验证9项浮窗生命周期；此测试需要开发用Node，插件运行不需要。

详细的移植结论、源码修改位置、测试与发布步骤见 [移植经验与后续开发](docs/MIGRATION.md)。
