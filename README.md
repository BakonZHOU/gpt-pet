# GPT Pet — 桌面软件版

Windows x64额度伙伴，保留原版鲸鱼娘设计，另有若叶睦和GPT娘白龙皮肤。默认人物约119像素，空闲不显示额度。

本仓库有两个独立版本：

- [`desktop`](https://github.com/BakonZHOU/gpt-pet/tree/desktop)：本分支，独立Windows桌宠。
- [`plugin`](https://github.com/BakonZHOU/gpt-pet/tree/plugin)：GPT应用内MCP App，无独立桌宠窗口。

## 安装

从[桌面版Releases](https://github.com/BakonZHOU/gpt-pet/releases/tag/desktop-v1.2.0)下载Windows ZIP，完整解压后双击 `安装.cmd`。自带.NET运行时，无需管理员权限。

安装后支持随GPT桌面客户端自动开关，Windows“设置 → 应用 → 已安装的应用”中可卸载。也可双击 `直接启动.cmd` 免安装运行。

## 功能

- 点击人物/气泡：额度 → 重置次数（概率触发语录）→ 隐藏；5秒自动收起。
- 5小时/每周剩余额度及重置时间；每下降1个百分点依次冒泡，去重并处理重置。
- 三套皮肤及蓝/浅绿/浅紫气泡主题，独立呆毛、呼吸、按压与音效。
- 右键设置，拖动、边缘吸附、镜像和自动隐藏任务栏避让。
- 语录可编辑；升级保留已有语录，位置与其他偏好保存在当前用户本地。

只读取已登录Codex的用量元数据，不发起模型推理，不兑换重置次数；不从单轮token数量估算额度。需要接收者自己安装并登录支持用量查询的Codex。

## 从源码构建

安装.NET 10 SDK，在Windows上运行：

```powershell
dotnet run --project tests/CodexPet.Tests.csproj -c Release
./tests/package-smoke.ps1
./build.ps1
./package.ps1
```

`build.ps1` 发布到 `dist/`；`package.ps1` 生成带运行时的ZIP及SHA256。界面自检：

```powershell
./dist/CodexPet.exe --ui-test "$PWD/work-ui-checks.txt"
```

不带 `--live` 的测试不需要登录账号。`--live` 只用于本机真实元数据接口检查。

## 许可与素材

原版设计和素材：MeteorNOX，MIT，见 `LICENSE.whale-widget`。新增皮肤的生成说明和提示词见 `docs/SKIN-*.txt`。本项目为非官方工具，与OpenAI无隶属关系。