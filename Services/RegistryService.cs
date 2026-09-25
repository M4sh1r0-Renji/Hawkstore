using Ravenhawk.Models;
using System.Net.Http;
using System.Text.Json;

namespace Ravenhawk.Services;

public sealed class RegistryService
{
    public const string DefaultIndexUrl = "https://raw.githubusercontent.com/M4sh1r0-Renji/hawkstore-registry/main/index.json";

    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<StoreIndex> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await Client.GetStreamAsync(DefaultIndexUrl, cancellationToken);
        var index = await JsonSerializer.DeserializeAsync<StoreIndex>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException(LocalizationService.Get("RegistryEmpty"));
        if (index.SchemaVersion != 1) throw new InvalidDataException(LocalizationService.Format("UnsupportedIndexVersion", index.SchemaVersion));
        return index;
    }

    public async Task<StorePackageManifest> LoadManifestAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException(LocalizationService.Get("InvalidManifestUrl"));
        await using var stream = await Client.GetStreamAsync(uri, cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<StorePackageManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException(LocalizationService.Get("ManifestEmpty"));
        if (manifest.SchemaVersion != 1) throw new InvalidDataException(LocalizationService.Format("UnsupportedManifestVersion", manifest.SchemaVersion));
        return manifest;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Hawkstore/0.2");
        return client;
    }
}
