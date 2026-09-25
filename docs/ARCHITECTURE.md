# Architecture

[简体中文](ARCHITECTURE.zh-CN.md) · English

Hawkstore is split into three trust boundaries:

1. **Desktop client** — reads the registry, downloads verified release assets, installs packages, and manages local plugins.
2. **Registry** — stores JSON metadata, package ownership, schemas, categories, and a generated searchable index. It does not store large DLL or ZIP payloads.
3. **Publishing pipeline** — validates author packages, inspects metadata, computes SHA-256, and prepares pull requests and immutable GitHub Releases.

```text
Author package -> validation -> registry pull request -> review/merge
                                                     |
                                                     v
GitHub Release asset <- release workflow <- registry metadata
        |
        v
Hawkstore client -> SHA-256 check -> BepInEx/plugins
```

The desktop client must never contain a repository-wide personal access token or push directly to the registry default branch.
