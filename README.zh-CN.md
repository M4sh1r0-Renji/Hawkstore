# Hawkstore

简体中文 · [English](README.md)

Hawkstore 是面向 Ravenfield BepInEx 5 插件的 Windows 桌面管理器。它采用简洁的 Windhawk 风格界面，提供安全的本地插件管理，并为后续基于 GitHub 的插件商店打下基础。

![Hawkstore 图标](Assets/hawkstore-icon.png)

## 当前功能

- 检测并安装 BepInEx 官方最新的 BepInEx 5 Windows 正式版。
- 根据 Ravenfield 可执行文件自动选择 x86 或 x64 安装包。
- 更新 BepInEx 前备份即将覆盖的文件。
- 扫描 `BepInEx/plugins` 中的顶层 DLL 和插件文件夹。
- 在 `plugins` 与 `plugins_disabled` 之间移动项目，实现启用和禁用。
- 以只读方式显示作者发布时填写的元数据，包括 SteamID64 和 Registry 验证状态。
- 在插件详细页编辑匹配的 BepInEx `.cfg` 和 `.json` 配置，包括带类型的数值、布尔值、枚举和键位。
- 从 DLL 元数据推断 BepInEx 插件 GUID，保留 CFG 注释与布局，校验声明类型和范围，并在每次保存配置前创建备份。
- 搜索已安装插件。
- 按更新时间、插件名称或已安装大小排序；启停插件不会重排卡片，只有主动排序或刷新才会重排。
- 在默认英语与简体中文界面之间切换。
- 在应用内“商店”页面浏览带版本协议的 GitHub Registry。
- 校验文件大小和 SHA-256 后安装或重新安装 ZIP 插件包。
- 阻止不安全的压缩包路径、备份已有插件，并保留原来的启用/禁用状态。
- 以 Dynamic Terrain Craters 1.9.0 作为首个示例订阅插件。
- 采用更紧凑的响应式卡片，根据窗口宽度自动切换每排一、二或三个插件。
- Ravenfield 运行时禁止安装和启停操作。

## 构建

需要 Windows 和 .NET 8 SDK。

```powershell
dotnet build .\Hawkstore.csproj -c Release
dotnet publish .\Hawkstore.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

默认游戏路径为 `D:\SteamLibrary\steamapps\common\Ravenfield`，可在应用内更改。

## 仓库体系

- `Hawkstore`：桌面客户端（当前仓库）
- `hawkstore-registry`：插件元数据、规范和可搜索索引
- `hawkstore-publishing`：作者打包与校验工具

参见[架构说明](docs/ARCHITECTURE.zh-CN.md)和[开发路线](docs/ROADMAP.zh-CN.md)。

## 安全模型

BepInEx 插件本质上是可执行代码。Hawkstore 会校验文件大小和 SHA-256、阻止压缩包路径穿越并备份已有安装，但不会宣称第三方插件绝对安全。新插件应通过 Pull Request 和自动校验进入 Registry，不允许客户端直接写入默认分支。

## 当前状态

本地管理器和首个 GitHub 在线浏览/安装闭环已经可用。下一阶段是版本与更新检测。
