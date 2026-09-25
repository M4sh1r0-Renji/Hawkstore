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
- 显示并编辑插件名称、作者、版本、游戏兼容性、更新时间和说明。
- 搜索已安装插件。
- 根据窗口宽度自动切换每排一、二或三个插件卡片。
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

BepInEx 插件本质上是可执行代码。商店安装功能完成后，Hawkstore 会校验包哈希，但不会宣称第三方插件绝对安全。新插件应通过 Pull Request 和自动校验进入 Registry，不允许客户端直接写入默认分支。

## 当前状态

本地管理器已经可用。下一阶段是实现 GitHub Registry 读取、在线浏览和经过哈希验证的安装流程。
