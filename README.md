# 额度伙伴 · GPT应用内版

界面通过MCP Apps在GPT面板内运行，使用官方全局/聊天侧栏入口；不调用独立桌宠程序，不安装Windows启动项，不启动HTTP服务。

## 使用

- 默认只显示人物。右键人物打开设置；点击外侧或Esc收起设置。
- 左键人物或气泡：额度 → 重置次数（按概率显示语录）→ 隐藏；5秒自动收起。
- 三套皮肤及配套气泡主题；呼吸、呆毛、按压、音效；呼吸和呆毛速度均为0.1–6秒。
- 额度每累计下降1个百分点排队冒泡；首次读取、重置和重复值不弹。手动点击取消排队。
- 设置内可编辑语录、查看两档重置时间、刷新和隐藏；隐藏或面板不活跃后暂停读取。
- 人物可在面板内拖动、吸附边缘；窗口缩小时自动调整位置和必要的尺寸，人物和气泡保持在可见区域。
- 画中画仅在GPT宿主声明支持时出现在右键设置中；窗口边框、尺寸和置顶由宿主管理。

## 数据和运行方式

本地stdio MCP后端由GPT启动，内附Windows x64 Node运行时，无需安装.NET或单独配置Node。后端只启动Codex的只读用量服务；传输层只允许initialize、initialized、account/rateLimits/read，不调用模型、不启动聊天、不兑换重置额度。

持续接收额度推送，10秒轮询兜底，3秒面板状态同步，文件活动合并刷新。仅监测会话文件的名称/修改信号，不读取聊天内容。多个面板使用活动租约，全部隐藏后释放连接；未正常关闭的面板最多20秒失效。

数据默认在%LOCALAPPDATA%/CodexPetApp。首次使用可迁移同一台电脑现有桌宠的偏好和语录；发布包不包含作者账号、密钥、个人偏好或聊天记录。

## 与独立桌宠的区别

额度、气泡、皮肤、语录、动画与点击顺序保留。拖动边界改为GPT面板可见区域。Windows任务栏避让、独立窗口置顶和开机启动属于桌面窗口行为，不适用于面板内界面；不能把本版描述为逐项保留全部桌面窗口能力。插件面板未挂载时不能保证在GPT其他页面显示提醒。

## 发布状态

本机开发版；未发布到公共插件市场。个人/仓库市场安装与公共目录上架不同。本插件读取本机额度，属于本地MCP插件；公共目录目前要求获准合作伙伴及组织白名单，并完成身份验证、审核和发布。

官方来源（2026-10-24核对）：

- https://developers.openai.com/plugins/build/plugins
- https://developers.openai.com/plugins/build/extensions
- https://developers.openai.com/plugins/build/chatgpt-ui
- https://developers.openai.com/plugins/deploy/desktop-only

## 验证范围

真实本机只读额度接口、MCP资源与工具、三次点击、单点和多点下降、去重、皮肤主题、0.1–6秒滑块和小窗口适配已验证。UI使用官方MCP Apps SDK和标准AppBridge测试容器验证；测试容器不进入发布包。尚未在当前GPT宿主窗口人工点击验证，需刷新插件入口或新开聊天加载新插件。不要把测试容器截图或CLI安装成功描述为公共市场已上架。

## GitHub版本与构建

本仓库的[`desktop`分支](https://github.com/BakonZHOU/gpt-pet/tree/desktop)是独立Windows软件，本分支`plugin`是GPT应用内版本。

下载[插件开发包](https://github.com/BakonZHOU/gpt-pet/releases/tag/plugin-v0.1.1)。这是本地开发/私有市场使用的MCP插件包，并不代表已经发布到OpenAI公共目录。

在Windows x64上安装Node.js 22或更新版本后：

```powershell
npm ci
npm run build
npm test
./package.ps1
```

生成`ui.bundle.js`和内附Node的Windows插件包。开发时可通过仓库根目录的`.agents/plugins/marketplace.json`加入本地插件市场；终端需安装支持插件的Codex。日常运行使用插件自带Node，不依赖开发者机器的绝对路径。

`npm test -- --live`仅在已安装并登录Codex的电脑上检查真实只读用量。CI不读取个人登录状态。