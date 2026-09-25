using Ravenhawk.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Ravenhawk.Services;

public sealed class PluginService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<PluginItem> Scan(string gameRoot)
    {
        var enabledRoot = Path.Combine(gameRoot, "BepInEx", "plugins");
        var disabledRoot = Path.Combine(gameRoot, "BepInEx", "plugins_disabled");
        Directory.CreateDirectory(enabledRoot);
        Directory.CreateDirectory(disabledRoot);

        var result = new List<PluginItem>();
        AddEntries(result, enabledRoot, true);
        AddEntries(result, disabledRoot, false);
        return result.OrderByDescending(x => x.IsEnabled).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddEntries(List<PluginItem> target, string root, bool enabled)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var info = new DirectoryInfo(dir);
            target.Add(CreateItem(dir, info.Name, true, enabled, info.LastWriteTimeUtc));
        }

        foreach (var dll in Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(dll);
            target.Add(CreateItem(dll, info.Name, false, enabled, info.LastWriteTimeUtc));
        }
    }

    private static PluginItem CreateItem(string path, string entryName, bool isDirectory, bool enabled, DateTime lastWriteUtc)
    {
        var manifestPath = GetManifestPath(path, isDirectory);
        PluginManifest manifest;
        if (File.Exists(manifestPath))
        {
            try { manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions) ?? new(); }
            catch { manifest = new(); }
        }
        else
        {
            manifest = InferManifest(path, entryName, isDirectory, lastWriteUtc);
        }

        if (string.IsNullOrWhiteSpace(manifest.Name)) manifest.Name = Path.GetFileNameWithoutExtension(entryName);
        if (string.IsNullOrWhiteSpace(manifest.Author)) manifest.Author = "未知作者";
        if (string.IsNullOrWhiteSpace(manifest.Version)) manifest.Version = "未知";
        if (string.IsNullOrWhiteSpace(manifest.SupportedGameVersion)) manifest.SupportedGameVersion = "未填写";
        if (string.IsNullOrWhiteSpace(manifest.Description)) manifest.Description = "暂无说明";
        manifest.LastUpdated ??= new DateTimeOffset(lastWriteUtc, TimeSpan.Zero);
        return new PluginItem { FullPath = path, EntryName = entryName, IsDirectory = isDirectory, IsEnabled = enabled, Manifest = manifest };
    }

    private static PluginManifest InferManifest(string path, string entryName, bool isDirectory, DateTime lastWriteUtc)
    {
        var dll = isDirectory ? Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).FirstOrDefault() : path;
        var manifest = new PluginManifest
        {
            Name = Path.GetFileNameWithoutExtension(entryName),
            LastUpdated = new DateTimeOffset(lastWriteUtc, TimeSpan.Zero)
        };

        if (dll is null) return manifest;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dll);
            if (!string.IsNullOrWhiteSpace(info.ProductName)) manifest.Name = info.ProductName!;
            if (!string.IsNullOrWhiteSpace(info.CompanyName)) manifest.Author = info.CompanyName!;
            if (!string.IsNullOrWhiteSpace(info.ProductVersion)) manifest.Version = info.ProductVersion!;
            if (!string.IsNullOrWhiteSpace(info.Comments)) manifest.Description = info.Comments!;
        }
        catch { /* Native or malformed DLL: filename metadata remains usable. */ }
        return manifest;
    }

    public void SaveManifest(PluginItem item, PluginManifest manifest)
    {
        manifest.LastUpdated = DateTimeOffset.Now;
        var path = GetManifestPath(item.FullPath, item.IsDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        item.Manifest = manifest;
        item.RefreshBindings();
    }

    public string Toggle(PluginItem item, string gameRoot)
    {
        EnsureGameNotRunning();
        var targetRoot = Path.Combine(gameRoot, "BepInEx", item.IsEnabled ? "plugins_disabled" : "plugins");
        Directory.CreateDirectory(targetRoot);
        var target = Path.Combine(targetRoot, item.EntryName);
        if (Directory.Exists(target) || File.Exists(target))
            throw new IOException($"目标位置已存在同名项目：{target}");

        if (item.IsDirectory)
        {
            Directory.Move(item.FullPath, target);
        }
        else
        {
            File.Move(item.FullPath, target);
            var oldManifest = GetManifestPath(item.FullPath, false);
            var newManifest = GetManifestPath(target, false);
            if (File.Exists(oldManifest)) File.Move(oldManifest, newManifest);
        }
        return target;
    }

    private static string GetManifestPath(string itemPath, bool isDirectory) =>
        isDirectory ? Path.Combine(itemPath, "ravenhawk.manifest.json") : itemPath + ".ravenhawk.json";

    public static void EnsureGameNotRunning()
    {
        if (Process.GetProcessesByName("ravenfield").Length > 0)
            throw new InvalidOperationException("Ravenfield 正在运行。请先关闭游戏，再更改插件或安装 BepInEx。");
    }
}
