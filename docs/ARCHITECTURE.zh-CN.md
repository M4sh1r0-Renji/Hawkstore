# 架构说明

简体中文 · [English](ARCHITECTURE.md)

Hawkstore 分为三个信任边界：

1. **桌面客户端**：读取 Registry、下载经过校验的 Release 资源、安装包并管理本地插件。
2. **Registry**：保存 JSON 元数据、包所有权、Schema、分类和生成的搜索索引，不保存大型 DLL 或 ZIP。
3. **发布流水线**：通过 Steam OpenID 取得作者的 SteamID64，校验作者包、检查元数据、计算 SHA-256，并创建 Pull Request 和不可变 GitHub Release。

```text
Steam OpenID -> 作者插件包 -> 自动校验 -> Registry Pull Request -> 审核/合并
                                                  |
                                                  v
GitHub Release 资源 <- 发布工作流 <- Registry 元数据
        |
        v
Hawkstore 客户端 -> SHA-256 校验 -> BepInEx/plugins
```

桌面客户端绝不能内置拥有整个仓库权限的 Personal Access Token，也不能直接推送 Registry 默认分支。
