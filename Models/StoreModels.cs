using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Ravenhawk.Models;

public sealed class StoreIndex
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("packages")]
    public List<StoreIndexEntry> Packages { get; set; } = [];
}

public sealed class StoreIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = "";

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("gameVersions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("manifestUrl")]
    public string ManifestUrl { get; set; } = "";

    [JsonPropertyName("installDirectory")]
    public string InstallDirectory { get; set; } = "";

    [JsonPropertyName("featured")]
    public bool Featured { get; set; }
}

public sealed class StorePackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("author")]
    public StoreAuthor Author { get; set; } = new();

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("game")]
    public StoreGame Game { get; set; } = new();

    [JsonPropertyName("plugin")]
    public StorePlugin Plugin { get; set; } = new();

    [JsonPropertyName("release")]
    public StoreRelease? Release { get; set; }
}

public sealed class StoreAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class StoreGame
{
    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = [];
}

public sealed class StorePlugin
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = "";

    [JsonPropertyName("installDirectory")]
    public string InstallDirectory { get; set; } = "";

    [JsonPropertyName("entryDll")]
    public string EntryDll { get; set; } = "";
}

public sealed class StoreRelease
{
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class StorePackageItem : INotifyPropertyChanged
{
    private bool _isInstalled;
    private bool _isInstalling;

    public required StoreIndexEntry Entry { get; init; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Author => Entry.Author;
    public string Description => Entry.Description;
    public string VersionText => $"v{Entry.LatestVersion}  ·  {Entry.Author}";
    public string CategoryText => string.Join("  ·  ", Entry.Categories);
    public string GameVersionText => $"支持 {string.Join(", ", Entry.GameVersions)}";
    public string BadgeText => Entry.Featured ? "示例订阅" : "";
    public string InstallState => IsInstalled ? "已安装" : "未安装";
    public string ActionText => IsInstalling ? "安装中…" : IsInstalled ? "重新安装" : "安装";
    public bool CanInstall => !IsInstalling;

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InstallState));
            OnPropertyChanged(nameof(ActionText));
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            if (_isInstalling == value) return;
            _isInstalling = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
