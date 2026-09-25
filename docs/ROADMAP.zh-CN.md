# 开发路线

简体中文 · [English](ROADMAP.md)

- [x] 阶段 1：本地插件扫描、只读元数据、启停和 BepInEx 5 安装器
- [x] 阶段 2：读取 GitHub Registry 并实现在线浏览页
- [x] 阶段 3：下载和安装 ZIP 插件包
- [x] 阶段 4：SHA-256 校验和安全解压
- [x] 阶段 4.1：带校验与备份的插件 CFG/JSON 配置编辑器
- [ ] 阶段 5：版本和更新检测
- [ ] 阶段 6：依赖解析
- [ ] 阶段 7：Steam OpenID 身份与 GitHub App 包所有权
- [ ] 阶段 8：作者上传界面
- [ ] 阶段 9：基于 Pull Request 的发布审核
- [ ] 阶段 10：自动 DLL 元数据校验和不可变 Release

紧接着要实现的是阶段 5：比较本地元数据与 Registry 版本，并提供明确的更新入口。
