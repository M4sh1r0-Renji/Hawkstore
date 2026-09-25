# Hawkstore

[简体中文](README.zh-CN.md) · English

Hawkstore is a Windows desktop manager for Ravenfield BepInEx 5 plugins. It combines a minimal Windhawk-inspired interface with safe local plugin management and lays the groundwork for a GitHub-backed package store.

![Hawkstore icon](Assets/hawkstore-icon.png)

## Current features

- Detect and install the latest official BepInEx 5 Windows release.
- Detect the Ravenfield executable architecture and select x86/x64 automatically.
- Back up files before a BepInEx upgrade.
- Scan top-level DLLs and plugin folders in `BepInEx/plugins`.
- Enable or disable plugins by moving them between `plugins` and `plugins_disabled`.
- Read and edit plugin name, author, version, game compatibility, update time, and description.
- Search installed plugins.
- Responsive one-, two-, or three-column card layout.
- Block install and enable/disable operations while Ravenfield is running.

## Build

Requirements: Windows and the .NET 8 SDK.

```powershell
dotnet build .\Hawkstore.csproj -c Release
dotnet publish .\Hawkstore.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The default game path is `D:\SteamLibrary\steamapps\common\Ravenfield` and can be changed in the app.

## Repository ecosystem

- `Hawkstore`: desktop client (this repository)
- `hawkstore-registry`: package metadata, schemas, and searchable index
- `hawkstore-publishing`: author packaging and validation tools

See [Architecture](docs/ARCHITECTURE.md) and [Roadmap](docs/ROADMAP.md).

## Security model

BepInEx plugins are executable code. Hawkstore verifies package hashes when store installation is implemented, but it will never claim that third-party plugins are completely safe. New packages should enter the registry through pull requests and automated validation, not direct writes to the default branch.

## Status

The local manager is working. The GitHub registry reader and online browse/install experience are the next implementation milestone.
