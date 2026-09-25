using Ravenhawk.Models;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ravenhawk.Services;

public sealed class GitHubPublisherService
{
    public const string OAuthClientId = "Ov23liL7N7y2YRKlASPG";
    private const string RegistryRepository = "M4sh1r0-Renji/hawkstore-registry";
    private static readonly Regex PackageIdRegex = new("^[a-z0-9]+(?:[._-][a-z0-9]+)+$", RegexOptions.Compiled);
    private static readonly Regex SemverRegex = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.Compiled);
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private string _token = "";

    public string Login { get; private set; } = "";
    public bool IsConnected => !string.IsNullOrWhiteSpace(_token);

    public GitHubPublisherService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Hawkstore/0.5");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task ConnectAsync(Action<GitHubDeviceCode> showCode, CancellationToken cancellationToken = default)
    {
        if (OAuthClientId.StartsWith("__", StringComparison.Ordinal))
            throw new InvalidOperationException(LocalizationService.Get("PublisherOAuthNotConfigured"));

        using var deviceRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = OAuthClientId, ["scope"] = "read:user public_repo" })
        };
        deviceRequest.Headers.Accept.Clear();
        deviceRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var deviceResponse = await _client.SendAsync(deviceRequest, cancellationToken);
        await EnsureSuccessAsync(deviceResponse, cancellationToken);
        var device = JsonNode.Parse(await deviceResponse.Content.ReadAsStringAsync(cancellationToken))?.AsObject()
            ?? throw new InvalidDataException(LocalizationService.Get("PublisherOAuthFailed"));
        var deviceCode = device["device_code"]?.GetValue<string>() ?? throw new InvalidDataException(LocalizationService.Get("PublisherOAuthFailed"));
        var interval = device["interval"]?.GetValue<int>() ?? 5;
        var info = new GitHubDeviceCode
        {
            UserCode = device["user_code"]?.GetValue<string>() ?? "",
            VerificationUri = device["verification_uri"]?.GetValue<string>() ?? "https://github.com/login/device",
            ExpiresIn = device["expires_in"]?.GetValue<int>() ?? 900
        };
        showCode(info);
        Process.Start(new ProcessStartInfo(info.VerificationUri) { UseShellExecute = true });

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = OAuthClientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                })
            };
            tokenRequest.Headers.Accept.Clear();
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var tokenResponse = await _client.SendAsync(tokenRequest, cancellationToken);
            await EnsureSuccessAsync(tokenResponse, cancellationToken);
            var tokenPayload = JsonNode.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken))?.AsObject();
            var accessToken = tokenPayload?["access_token"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                _token = accessToken;
                break;
            }
            var error = tokenPayload?["error"]?.GetValue<string>();
            if (error == "authorization_pending") continue;
            if (error == "slow_down") { interval += 5; continue; }
            throw new InvalidOperationException(tokenPayload?["error_description"]?.GetValue<string>() ?? LocalizationService.Get("PublisherOAuthFailed"));
        }

        var user = await SendJsonAsync(HttpMethod.Get, "https://api.github.com/user", null, cancellationToken);
        Login = user?["login"]?.GetValue<string>() ?? throw new InvalidDataException(LocalizationService.Get("PublisherOAuthFailed"));
    }

    public async Task<GitHubPublishResult> PublishAsync(PublisherProject project, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Validate(project, requireSource: true);
        EnsureConnected();
        progress?.Report(LocalizationService.Get("PublisherPreparingPackage"));
        var artifact = await PrepareArtifactAsync(project, cancellationToken);
        try
        {
            var repository = await EnsureRepositoryAsync(project, cancellationToken);
            project.RepositoryFullName = repository.FullName;
            progress?.Report(LocalizationService.Get("PublisherCreatingRelease"));
            var release = await CreateReleaseAsync(project, repository.FullName, cancellationToken);
            var asset = await UploadAssetAsync(repository.FullName, release.Id, artifact.Path, cancellationToken);
            project.ReleaseId = release.Id;
            project.DownloadUrl = asset.DownloadUrl;
            project.Sha256 = artifact.Sha256;
            project.AssetSize = artifact.Size;
            project.LastPublishedAt = DateTimeOffset.UtcNow;

            progress?.Report(LocalizationService.Get("PublisherUploadingMetadata"));
            var manifest = BuildManifest(project, Login);
            await PutContentAsync(repository.FullName, "manifest.json", manifest, $"Publish {project.Name} {project.Version}", cancellationToken);
            await PutContentAsync(repository.FullName, "README.md", BuildReadme(project), $"Update {project.Name} metadata", cancellationToken);
            var issue = await UpsertSubmissionIssueAsync(project, manifest, cancellationToken);
            project.SubmissionIssueNumber = issue.Number;
            project.Published = true;
            return new GitHubPublishResult
            {
                RepositoryUrl = $"https://github.com/{repository.FullName}",
                ReleaseUrl = release.HtmlUrl,
                SubmissionUrl = issue.HtmlUrl
            };
        }
        finally
        {
            if (artifact.DeleteAfterUse && File.Exists(artifact.Path)) File.Delete(artifact.Path);
        }
    }

    public async Task<GitHubPublishResult> UpdateMetadataAsync(PublisherProject project, CancellationToken cancellationToken = default)
    {
        Validate(project, requireSource: false);
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(project.RepositoryFullName) || !project.Published)
            throw new InvalidOperationException(LocalizationService.Get("PublisherNotPublished"));
        var manifest = BuildManifest(project, Login);
        await PutContentAsync(project.RepositoryFullName, "manifest.json", manifest, $"Update {project.Name} metadata", cancellationToken);
        await PutContentAsync(project.RepositoryFullName, "README.md", BuildReadme(project), $"Update {project.Name} README", cancellationToken);
        if (project.ReleaseId > 0)
            await SendJsonAsync(HttpMethod.Patch, $"https://api.github.com/repos/{project.RepositoryFullName}/releases/{project.ReleaseId}", new JsonObject
            {
                ["name"] = $"{project.Name} {project.Version}", ["body"] = project.Description
            }, cancellationToken);
        var issue = await UpsertSubmissionIssueAsync(project, manifest, cancellationToken);
        project.SubmissionIssueNumber = issue.Number;
        return new GitHubPublishResult
        {
            RepositoryUrl = $"https://github.com/{project.RepositoryFullName}",
            ReleaseUrl = $"https://github.com/{project.RepositoryFullName}/releases/tag/v{project.Version}",
            SubmissionUrl = issue.HtmlUrl
        };
    }

    public async Task UnpublishAsync(PublisherProject project, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(project.RepositoryFullName)) return;
        if (project.ReleaseId > 0)
        {
            await SendAllowNotFoundAsync(HttpMethod.Delete, $"https://api.github.com/repos/{project.RepositoryFullName}/releases/{project.ReleaseId}", null, cancellationToken);
            await SendAllowNotFoundAsync(HttpMethod.Delete, $"https://api.github.com/repos/{project.RepositoryFullName}/git/refs/tags/v{project.Version}", null, cancellationToken);
        }
        if (project.SubmissionIssueNumber > 0)
        {
            await SendJsonAsync(HttpMethod.Patch, $"https://api.github.com/repos/{RegistryRepository}/issues/{project.SubmissionIssueNumber}", new JsonObject
            {
                ["state"] = "closed",
                ["body"] = BuildIssueBody(project, BuildManifest(project, Login)) + "\n\n> This submission was withdrawn by the author."
            }, cancellationToken);
        }
        project.Published = false;
        project.ReleaseId = 0;
        project.DownloadUrl = "";
        project.Sha256 = "";
        project.AssetSize = 0;
    }

    private async Task<(string Path, long Size, string Sha256, bool DeleteAfterUse)> PrepareArtifactAsync(PublisherProject project, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(project.SourceFile);
        if (!File.Exists(source)) throw new FileNotFoundException(LocalizationService.Get("PublisherSourceMissing"), source);
        string artifactPath;
        var delete = false;
        if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            artifactPath = Path.Combine(Path.GetTempPath(), $"{project.Id}-{project.Version}-{Guid.NewGuid():N}.zip");
            await using var output = File.Create(artifactPath);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
            var entry = archive.CreateEntry($"BepInEx/plugins/{project.InstallDirectory}/{project.EntryDll}", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var input = File.OpenRead(source);
            await input.CopyToAsync(entryStream, cancellationToken);
            delete = true;
        }
        else if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(source);
            if (!archive.Entries.Any(x => x.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(LocalizationService.Get("PublisherZipMissingDll"));
            artifactPath = source;
        }
        else throw new InvalidDataException(LocalizationService.Get("PublisherSourceTypeInvalid"));

        await using var hashStream = File.OpenRead(artifactPath);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
        return (artifactPath, new FileInfo(artifactPath).Length, sha, delete);
    }

    private async Task<(string FullName, string DefaultBranch)> EnsureRepositoryAsync(PublisherProject project, CancellationToken cancellationToken)
    {
        var repositoryName = string.IsNullOrWhiteSpace(project.RepositoryFullName)
            ? "hawkstore-" + Regex.Replace(project.Id.ToLowerInvariant(), "[^a-z0-9-]+", "-").Trim('-')
            : project.RepositoryFullName.Split('/').Last();
        var existing = await TryGetJsonAsync($"https://api.github.com/repos/{Login}/{repositoryName}", cancellationToken);
        if (existing is null)
        {
            existing = await SendJsonAsync(HttpMethod.Post, "https://api.github.com/user/repos", new JsonObject
            {
                ["name"] = repositoryName,
                ["description"] = project.Description,
                ["private"] = false,
                ["auto_init"] = true
            }, cancellationToken);
        }
        return (existing?["full_name"]?.GetValue<string>() ?? $"{Login}/{repositoryName}", existing?["default_branch"]?.GetValue<string>() ?? "main");
    }

    private async Task<(long Id, string HtmlUrl)> CreateReleaseAsync(PublisherProject project, string repository, CancellationToken cancellationToken)
    {
        var tag = "v" + project.Version;
        if (await TryGetJsonAsync($"https://api.github.com/repos/{repository}/releases/tags/{tag}", cancellationToken) is not null)
            throw new InvalidOperationException(LocalizationService.Get("PublisherVersionExists"));
        var release = await SendJsonAsync(HttpMethod.Post, $"https://api.github.com/repos/{repository}/releases", new JsonObject
        {
            ["tag_name"] = tag,
            ["name"] = $"{project.Name} {project.Version}",
            ["body"] = project.Description,
            ["draft"] = false,
            ["prerelease"] = false
        }, cancellationToken);
        return (release?["id"]?.GetValue<long>() ?? 0, release?["html_url"]?.GetValue<string>() ?? "");
    }

    private async Task<(string DownloadUrl, string Name)> UploadAssetAsync(string repository, long releaseId, string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        using var request = CreateRequest(HttpMethod.Post, $"https://uploads.github.com/repos/{repository}/releases/{releaseId}/assets?name={Uri.EscapeDataString(file.Name)}");
        request.Content = new StreamContent(File.OpenRead(path));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (json?["browser_download_url"]?.GetValue<string>() ?? "", json?["name"]?.GetValue<string>() ?? file.Name);
    }

    private async Task PutContentAsync(string repository, string path, string content, string message, CancellationToken cancellationToken)
    {
        var existing = await TryGetJsonAsync($"https://api.github.com/repos/{repository}/contents/{path}", cancellationToken);
        var body = new JsonObject
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        };
        if (existing?["sha"] is JsonNode sha) body["sha"] = sha.GetValue<string>();
        await SendJsonAsync(HttpMethod.Put, $"https://api.github.com/repos/{repository}/contents/{path}", body, cancellationToken);
    }

    private async Task<(int Number, string HtmlUrl)> UpsertSubmissionIssueAsync(PublisherProject project, string manifest, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["title"] = $"Mod submission: {project.Name} {project.Version}",
            ["body"] = BuildIssueBody(project, manifest)
        };
        JsonNode? issue;
        if (project.SubmissionIssueNumber > 0)
            issue = await SendJsonAsync(HttpMethod.Patch, $"https://api.github.com/repos/{RegistryRepository}/issues/{project.SubmissionIssueNumber}", body, cancellationToken);
        else
            issue = await SendJsonAsync(HttpMethod.Post, $"https://api.github.com/repos/{RegistryRepository}/issues", body, cancellationToken);
        return (issue?["number"]?.GetValue<int>() ?? 0, issue?["html_url"]?.GetValue<string>() ?? "");
    }

    private static string BuildIssueBody(PublisherProject project, string manifest) =>
        $"Automated Hawkstore author submission.\n\nRepository: https://github.com/{project.RepositoryFullName}\nRelease asset: {project.DownloadUrl}\n\n```json\n{manifest}\n```\n\nThe Registry maintainers must review this submission before it appears in the Store.";

    private static string BuildReadme(PublisherProject project) =>
        $"# {project.Name}\n\n{project.Description}\n\n- Version: `{project.Version}`\n- Ravenfield: `{project.GameVersion}`\n- Plugin GUID: `{project.PluginGuid}`\n- Author SteamID64: `{project.SteamId}` (verified with Steam OpenID)\n";

    private static string BuildManifest(PublisherProject project, string login)
    {
        var categories = project.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var gameVersions = project.GameVersion.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var manifest = new JsonObject
        {
            ["$schema"] = "https://raw.githubusercontent.com/M4sh1r0-Renji/hawkstore-registry/main/schemas/manifest.schema.json",
            ["schemaVersion"] = 1,
            ["id"] = project.Id,
            ["name"] = project.Name,
            ["version"] = project.Version,
            ["author"] = new JsonObject
            {
                ["name"] = project.AuthorName,
                ["github"] = login,
                ["steamId"] = project.SteamId,
                ["steamVerification"] = new JsonObject
                {
                    ["provider"] = "steam-openid",
                    ["status"] = project.SteamVerified ? "verified" : "unverified",
                    ["verifiedAt"] = project.SteamVerifiedAt?.ToString("O")
                }
            },
            ["owners"] = new JsonArray(login),
            ["description"] = project.Description,
            ["categories"] = new JsonArray(categories.Select(x => (JsonNode?)x).ToArray()),
            ["game"] = new JsonObject { ["id"] = "ravenfield", ["versions"] = new JsonArray(gameVersions.Select(x => (JsonNode?)x).ToArray()) },
            ["bepInEx"] = new JsonObject { ["version"] = "5.4.23" },
            ["plugin"] = new JsonObject
            {
                ["guid"] = project.PluginGuid,
                ["installDirectory"] = project.InstallDirectory,
                ["entryDll"] = project.EntryDll
            },
            ["dependencies"] = new JsonArray(),
            ["links"] = new JsonObject { ["source"] = $"https://github.com/{project.RepositoryFullName}" },
            ["release"] = new JsonObject
            {
                ["downloadUrl"] = project.DownloadUrl,
                ["sha256"] = project.Sha256,
                ["size"] = project.AssetSize,
                ["publishedAt"] = (project.LastPublishedAt ?? DateTimeOffset.UtcNow).ToString("O")
            }
        };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Validate(PublisherProject project, bool requireSource)
    {
        if (!PackageIdRegex.IsMatch(project.Id)) throw new InvalidDataException(LocalizationService.Get("PublisherIdInvalid"));
        if (!SemverRegex.IsMatch(project.Version)) throw new InvalidDataException(LocalizationService.Get("PublisherVersionInvalid"));
        if (string.IsNullOrWhiteSpace(project.Name) || string.IsNullOrWhiteSpace(project.AuthorName) || string.IsNullOrWhiteSpace(project.Description))
            throw new InvalidDataException(LocalizationService.Get("PublisherRequiredFields"));
        if (!Regex.IsMatch(project.SteamId, "^7656119[0-9]{10}$")) throw new InvalidDataException(LocalizationService.Get("PublisherSteamIdInvalid"));
        if (!project.SteamVerified) throw new InvalidDataException(LocalizationService.Get("PublisherSteamVerificationRequired"));
        if (Path.GetFileName(project.InstallDirectory) != project.InstallDirectory || string.IsNullOrWhiteSpace(project.InstallDirectory))
            throw new InvalidDataException(LocalizationService.Get("InvalidInstallDirectory"));
        if (!project.EntryDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(project.EntryDll) != project.EntryDll)
            throw new InvalidDataException(LocalizationService.Get("InvalidEntryDll"));
        if (requireSource && !File.Exists(project.SourceFile)) throw new FileNotFoundException(LocalizationService.Get("PublisherSourceMissing"));
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException(LocalizationService.Get("PublisherConnectFirst"));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private async Task<JsonNode?> SendJsonAsync(HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, url);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task<JsonNode?> TryGetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task SendAllowNotFoundAsync(HttpMethod method, string url, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, url);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        try { detail = JsonNode.Parse(detail)?["message"]?.GetValue<string>() ?? detail; } catch { }
        throw new HttpRequestException($"GitHub {(int)response.StatusCode}: {detail}");
    }
}
