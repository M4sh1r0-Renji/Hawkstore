using System.Text.Json.Serialization;

namespace Ravenhawk.Models;

public sealed class PluginManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "未知作者";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "未知";

    [JsonPropertyName("supportedGameVersion")]
    public string SupportedGameVersion { get; set; } = "未填写";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset? LastUpdated { get; set; }

    [JsonPropertyName("authorSteamId")]
    public string AuthorSteamId { get; set; } = "";

    [JsonPropertyName("authorSteamVerified")]
    public bool AuthorSteamVerified { get; set; }

    [JsonPropertyName("guid")]
    public string PluginGuid { get; set; } = "";
}
