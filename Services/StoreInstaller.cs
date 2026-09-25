using Ravenhawk.Models;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ravenhawk.Services;

public sealed class StoreInstaller
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> InstallAsync(StorePackageManifest manifest, string gameRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        PluginService.EnsureGameNotRunning();
        ValidateManifest(manifest);

        var bepinExRoot = Path.Combine(gameRoot, "BepInEx");
        var enabledRoot = Path.Combine(bepinExRoot, "plugins");
        var disabledRoot = Path.Combine(bepinExRoot, "plugins_disabled");
        Directory.CreateDirectory(enabledRoot);
        Directory.CreateDirectory(disabledRoot);

        var stagingRoot = Path.Combine(bepinExRoot, ".hawkstore-staging", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingRoot, "package.zip");
        var extractRoot = Path.Combine(stagingRoot, "extracted");
        Directory.CreateDirectory(extractRoot);

        try
        {
            progress?.Report(LocalizationService.Format("StoreDownloading", manifest.Name, manifest.Version));
            using (var response = await Client.GetAsync(manifest.Release!.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(archivePath);
                await input.CopyToAsync(output, cancellationToken);
            }

            var archiveInfo = new FileInfo(archivePath);
            if (archiveInfo.Length != manifest.Release.Size)
                throw new InvalidDataException(LocalizationService.Format("SizeMismatch", manifest.Release.Size, archiveInfo.Length));

            progress?.Report(LocalizationService.Get("VerifyingPackage"));
            await using (var stream = File.OpenRead(archivePath))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!digest.Equals(manifest.Release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(LocalizationService.Get("HashMismatch"));
            }

            progress?.Report(LocalizationService.Get("ExtractingPackage"));
            ExtractSafely(archivePath, extractRoot);
            var packageRoot = FindPackageRoot(extractRoot, manifest.Plugin.InstallDirectory);
            var entryDll = Path.Combine(packageRoot, manifest.Plugin.EntryDll);
            if (!File.Exists(entryDll)) throw new InvalidDataException(LocalizationService.Format("MissingEntryDll", manifest.Plugin.EntryDll));

            var enabledPath = Path.Combine(enabledRoot, manifest.Plugin.InstallDirectory);
            var disabledPath = Path.Combine(disabledRoot, manifest.Plugin.InstallDirectory);
            if (Directory.Exists(enabledPath) && Directory.Exists(disabledPath))
                throw new IOException(LocalizationService.Get("DuplicatePluginRoots"));

            var existingPath = Directory.Exists(disabledPath) ? disabledPath : Directory.Exists(enabledPath) ? enabledPath : null;
            var destination = existingPath ?? enabledPath;
            string? backupPath = null;
            if (existingPath is not null)
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                backupPath = Path.Combine(bepinExRoot, "hawkstore_backups", manifest.Id, $"{stamp}-{manifest.Version}");
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                progress?.Report(LocalizationService.Get("BackingUpPlugin"));
                Directory.Move(existingPath, backupPath);
            }

            try
            {
                Directory.Move(packageRoot, destination);
                WriteLocalManifest(destination, manifest);
            }
            catch
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                if (backupPath is not null && Directory.Exists(backupPath)) Directory.Move(backupPath, destination);
                throw;
            }

            progress?.Report(LocalizationService.Format("StoreInstalledProgress", manifest.Name, manifest.Version));
            return destination;
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, true); }
                catch { /* A failed cleanup must not hide the installation result. */ }
            }
        }
    }

    private static void ValidateManifest(StorePackageManifest manifest)
    {
        if (manifest.Release is null) throw new InvalidDataException(LocalizationService.Get("ReleaseMissing"));
        if (!Uri.TryCreate(manifest.Release.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException(LocalizationService.Get("HttpsRequired"));
        if (manifest.Release.Size <= 0 || manifest.Release.Sha256.Length != 64)
            throw new InvalidDataException(LocalizationService.Get("ReleaseIncomplete"));
        var installDirectory = manifest.Plugin.InstallDirectory;
        if (string.IsNullOrWhiteSpace(installDirectory) || Path.GetFileName(installDirectory) != installDirectory)
            throw new InvalidDataException(LocalizationService.Get("InvalidInstallDirectory"));
        if (string.IsNullOrWhiteSpace(manifest.Plugin.EntryDll) || Path.GetFileName(manifest.Plugin.EntryDll) != manifest.Plugin.EntryDll)
            throw new InvalidDataException(LocalizationService.Get("InvalidEntryDll"));
    }

    private static void ExtractSafely(string archivePath, string extractRoot)
    {
        var normalizedRoot = Path.GetFullPath(extractRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative)) continue;
            var destination = Path.GetFullPath(Path.Combine(extractRoot, relative));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Format("UnsafeArchivePath", entry.FullName));
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static string FindPackageRoot(string extractRoot, string installDirectory)
    {
        var direct = Path.Combine(extractRoot, installDirectory);
        if (Directory.Exists(direct)) return direct;
        var publishingLayout = Path.Combine(extractRoot, "BepInEx", "plugins", installDirectory);
        if (Directory.Exists(publishingLayout)) return publishingLayout;
        throw new InvalidDataException(LocalizationService.Format("PackageDirectoryMissing", installDirectory));
    }

    private static void WriteLocalManifest(string destination, StorePackageManifest manifest)
    {
        var local = new PluginManifest
        {
            Name = manifest.Name,
            Author = manifest.Author.Name,
            Version = manifest.Version,
            SupportedGameVersion = string.Join(", ", manifest.Game.Versions),
            Description = manifest.Description,
            LastUpdated = manifest.Release?.PublishedAt ?? DateTimeOffset.Now,
            AuthorSteamId = manifest.Author.SteamId,
            AuthorSteamVerified = manifest.Author.SteamVerification?.Provider.Equals("steam-openid", StringComparison.OrdinalIgnoreCase) == true
                && manifest.Author.SteamVerification.Status.Equals("verified", StringComparison.OrdinalIgnoreCase)
        };
        File.WriteAllText(Path.Combine(destination, "ravenhawk.manifest.json"), JsonSerializer.Serialize(local, JsonOptions));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Hawkstore/0.2");
        return client;
    }
}
