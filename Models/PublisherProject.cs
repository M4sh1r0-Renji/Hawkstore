using System.Text.Json.Serialization;

namespace Ravenhawk.Models;

public sealed class PublisherProject
{
    public string Id { get; set; } = "com.example.ravenfield.myplugin";
    public string Name { get; set; } = "My Ravenfield Plugin";
    public string Version { get; set; } = "1.0.0";
    public string AuthorName { get; set; } = "";
    public string SteamId { get; set; } = "";
    public bool SteamVerified { get; set; }
    public DateTimeOffset? SteamVerifiedAt { get; set; }
    public string Description { get; set; } = "";
    public string GameVersion { get; set; } = "EA38";
    public string Categories { get; set; } = "Gameplay";
    public string PluginGuid { get; set; } = "com.example.ravenfield.myplugin";
    public string InstallDirectory { get; set; } = "MyPlugin";
    public string EntryDll { get; set; } = "MyPlugin.dll";
    public string SourceFile { get; set; } = "";
    public string RepositoryFullName { get; set; } = "";
    public long ReleaseId { get; set; }
    public int SubmissionIssueNumber { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long AssetSize { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset? LastPublishedAt { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name}  v{Version}";
}

public sealed class SteamIdentity
{
    public string SteamId { get; init; } = "";
    public string PersonaName { get; init; } = "";
}

public sealed class GitHubDeviceCode
{
    public string UserCode { get; init; } = "";
    public string VerificationUri { get; init; } = "";
    public int ExpiresIn { get; init; }
}

public sealed class GitHubPublishResult
{
    public string RepositoryUrl { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string SubmissionUrl { get; init; } = "";
}
