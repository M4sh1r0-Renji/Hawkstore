using Ravenhawk.Models;
using System.Text.Json;

namespace Ravenhawk.Services;

public sealed class PublisherProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hawkstore", "publisher-projects.json");

    public List<PublisherProject> Load()
    {
        if (!File.Exists(_path)) return [];
        try { return JsonSerializer.Deserialize<List<PublisherProject>>(File.ReadAllText(_path), JsonOptions) ?? []; }
        catch { return []; }
    }

    public void Save(IEnumerable<PublisherProject> projects)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(projects, JsonOptions));
        File.Move(temporary, _path, true);
    }
}
