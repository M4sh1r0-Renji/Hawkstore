using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Ravenhawk.Services;

public sealed class BepInExInstaller
{
    private readonly HttpClient _http = new();
    public BepInExInstaller()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hawkstore", "0.2"));
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<string> InstallLatestV5Async(string gameRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        PluginService.EnsureGameNotRunning();
        ValidateGameRoot(gameRoot);
        progress?.Report(LocalizationService.Get("BepQuery"));

        using var response = await _http.GetAsync("https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=30", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var release = document.RootElement.EnumerateArray().FirstOrDefault(x =>
            !x.GetProperty("draft").GetBoolean() &&
            x.GetProperty("tag_name").GetString()?.StartsWith("v5.", StringComparison.OrdinalIgnoreCase) == true);
        if (release.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException(LocalizationService.Get("BepNoRelease"));

        var architecture = DetectArchitecture(gameRoot);
        var marker = architecture == "x86" ? "win_x86" : "win_x64";
        var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(x =>
            x.GetProperty("name").GetString()?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true &&
            x.GetProperty("name").GetString()?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException(LocalizationService.Format("BepNoAsset", marker));

        var version = release.GetProperty("tag_name").GetString() ?? "v5";
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var tempFile = Path.Combine(Path.GetTempPath(), $"Hawkstore-BepInEx-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report(LocalizationService.Format("BepDownloading", version, architecture));
            using (var download = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(tempFile);
                await input.CopyToAsync(output, cancellationToken);
            }

            progress?.Report(LocalizationService.Get("BepInstalling"));
            ExtractWithBackup(tempFile, gameRoot);
            File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "hawkstore-bepinex-version.txt"), version);
            progress?.Report(LocalizationService.Format("BepComplete", version));
            return version;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static void ValidateGameRoot(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(LocalizationService.Get("GameDirectoryMissing"));
        if (!Directory.EnumerateFiles(root, "ravenfield*.exe", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidOperationException(LocalizationService.Get("GameExecutableMissing"));
    }

    private static string DetectArchitecture(string root)
    {
        var exe = Directory.EnumerateFiles(root, "ravenfield*.exe", SearchOption.TopDirectoryOnly).First();
        try
        {
            using var stream = File.OpenRead(exe);
            using var pe = new PEReader(stream);
            return pe.PEHeaders.CoffHeader.Machine == System.Reflection.PortableExecutable.Machine.I386 ? "x86" : "x64";
        }
        catch { return "x64"; }
    }

    private static void ExtractWithBackup(string zipPath, string gameRoot)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var rootFull = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var backupRoot = Path.Combine(gameRoot, "BepInEx", "hawkstore-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var backedUp = false;

        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(gameRoot, entry.FullName));
            if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Get("UnsafeBepArchive"));
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                var relative = Path.GetRelativePath(gameRoot, destination);
                var backup = Path.Combine(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, true);
                backedUp = true;
            }
            entry.ExtractToFile(destination, true);
        }

        if (!backedUp && Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
    }
}
