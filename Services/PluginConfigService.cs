using Ravenhawk.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ravenhawk.Services;

public sealed class PluginConfigService
{
    public IReadOnlyList<PluginConfigFile> FindForPlugin(string gameRoot, PluginItem plugin)
    {
        var configRoot = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "config"));
        if (!Directory.Exists(configRoot)) return [];

        var identities = new[]
        {
            plugin.Manifest.PluginGuid,
            plugin.DisplayName,
            Path.GetFileNameWithoutExtension(plugin.EntryName)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize).Where(x => x.Length >= 5).Distinct().ToArray();

        return Directory.EnumerateFiles(configRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).Equals("BepInEx.cfg", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Score = MatchScore(path, identities, plugin.Manifest.PluginGuid) })
            .Where(x => x.Score >= 70)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Path.GetFileName(x.Path), StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new PluginConfigFile
            {
                FullPath = x.Path,
                Format = x.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? PluginConfigFormat.Json : PluginConfigFormat.BepInExCfg
            })
            .ToList();
    }

    public PluginConfigFile Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? PluginConfigFormat.Json : PluginConfigFormat.BepInExCfg;
        var file = new PluginConfigFile
        {
            FullPath = path,
            Format = format,
            OriginalText = text,
            NewLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
            HasUtf8Bom = hasBom
        };
        if (format == PluginConfigFormat.BepInExCfg) ParseCfg(file);
        else ParseJson(file);
        return file;
    }

    public string Save(PluginConfigFile file, string gameRoot)
    {
        PluginService.EnsureGameNotRunning();
        EnsureInsideConfigDirectory(file.FullPath, gameRoot);
        foreach (var entry in file.Entries) Validate(entry);

        var backupRoot = Path.Combine(gameRoot, "BepInEx", "hawkstore_backups", "config", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(backupRoot, Path.GetFileName(file.FullPath));
        File.Copy(file.FullPath, backupPath, true);

        var output = file.Format == PluginConfigFormat.BepInExCfg ? BuildCfg(file) : BuildJson(file);
        var encoding = new UTF8Encoding(file.HasUtf8Bom);
        var temporaryPath = file.FullPath + ".hawkstore.tmp";
        try
        {
            File.WriteAllText(temporaryPath, output, encoding);
            File.Move(temporaryPath, file.FullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        return backupPath;
    }

    private static void ParseCfg(PluginConfigFile file)
    {
        file.Lines = file.OriginalText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var section = "";
        var descriptions = new List<string>();
        var type = "";
        var defaultValue = "";
        var allowedValues = new List<string>();
        double? minimumValue = null;
        double? maximumValue = null;

        for (var i = 0; i < file.Lines.Count; i++)
        {
            var trimmed = file.Lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                descriptions.Clear();
                type = "";
                defaultValue = "";
                allowedValues.Clear();
                minimumValue = null;
                maximumValue = null;
                continue;
            }
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                var description = trimmed[3..].Trim();
                if (!description.StartsWith("Settings file was created", StringComparison.OrdinalIgnoreCase) &&
                    !description.StartsWith("Plugin GUID:", StringComparison.OrdinalIgnoreCase))
                    descriptions.Add(description);
                continue;
            }
            if (trimmed.StartsWith("# Setting type:", StringComparison.OrdinalIgnoreCase))
            {
                type = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                continue;
            }
            if (trimmed.StartsWith("# Default value:", StringComparison.OrdinalIgnoreCase))
            {
                defaultValue = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                continue;
            }
            if (trimmed.StartsWith("# Acceptable values:", StringComparison.OrdinalIgnoreCase))
            {
                allowedValues = trimmed[(trimmed.IndexOf(':') + 1)..].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                continue;
            }
            if (trimmed.StartsWith("# Acceptable value range:", StringComparison.OrdinalIgnoreCase))
            {
                var range = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                if (range.StartsWith("From ", StringComparison.OrdinalIgnoreCase))
                {
                    var separator = range.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
                    if (separator > 5 &&
                        double.TryParse(range[5..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) &&
                        double.TryParse(range[(separator + 4)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum))
                    {
                        minimumValue = minimum;
                        maximumValue = maximum;
                    }
                }
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
            var equals = file.Lines[i].IndexOf('=');
            if (equals <= 0) continue;
            var key = file.Lines[i][..equals].Trim();
            var value = file.Lines[i][(equals + 1)..].Trim();
            file.Entries.Add(new PluginConfigEntry
            {
                Section = section,
                Key = key,
                Description = string.Join(" ", descriptions),
                TypeName = type,
                DefaultValue = defaultValue,
                AllowedValues = allowedValues.ToArray(),
                MinimumValue = minimumValue,
                MaximumValue = maximumValue,
                LineIndex = i,
                ValueKind = InferCfgKind(type, value),
                Value = value
            });
            descriptions.Clear();
            type = "";
            defaultValue = "";
            allowedValues = [];
            minimumValue = null;
            maximumValue = null;
        }
    }

    private static void ParseJson(PluginConfigFile file)
    {
        var root = JsonNode.Parse(file.OriginalText, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidDataException(LocalizationService.Get("ConfigJsonEmpty"));
        AddJsonEntries(file.Entries, root, [], "");
    }

    private static void AddJsonEntries(List<PluginConfigEntry> target, JsonNode? node, List<string> path, string section)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                var childPath = new List<string>(path) { EscapePointer(pair.Key) };
                AddJsonEntries(target, pair.Value, childPath, path.Count == 0 ? pair.Key : section);
            }
            return;
        }
        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var childPath = new List<string>(path) { i.ToString(CultureInfo.InvariantCulture) };
                AddJsonEntries(target, array[i], childPath, section);
            }
            return;
        }

        var value = node as JsonValue;
        var (kind, text, typeName) = ReadJsonValue(value);
        target.Add(new PluginConfigEntry
        {
            Section = section,
            Key = path.Count == 0 ? "$" : UnescapePointer(path[^1]),
            Locator = "/" + string.Join('/', path),
            TypeName = typeName,
            ValueKind = kind,
            Value = text
        });
    }

    private static (PluginConfigValueKind Kind, string Text, string TypeName) ReadJsonValue(JsonValue? value)
    {
        if (value is null) return (PluginConfigValueKind.JsonNull, "null", "null");
        if (value.TryGetValue<bool>(out var boolean)) return (PluginConfigValueKind.JsonBoolean, boolean ? "true" : "false", "Boolean");
        if (value.TryGetValue<long>(out var integer)) return (PluginConfigValueKind.JsonInteger, integer.ToString(CultureInfo.InvariantCulture), "Integer");
        if (value.TryGetValue<double>(out var number)) return (PluginConfigValueKind.JsonNumber, number.ToString("R", CultureInfo.InvariantCulture), "Number");
        if (value.TryGetValue<string>(out var text)) return (PluginConfigValueKind.JsonString, text, "String");
        return (PluginConfigValueKind.JsonString, value.ToJsonString(), "String");
    }

    private static string BuildCfg(PluginConfigFile file)
    {
        var lines = file.Lines.ToArray();
        foreach (var entry in file.Entries)
        {
            if (entry.LineIndex < 0 || entry.LineIndex >= lines.Length) continue;
            var equals = lines[entry.LineIndex].IndexOf('=');
            if (equals < 0) continue;
            lines[entry.LineIndex] = lines[entry.LineIndex][..(equals + 1)] + " " + entry.Value;
        }
        return string.Join(file.NewLine, lines);
    }

    private static string BuildJson(PluginConfigFile file)
    {
        var root = JsonNode.Parse(file.OriginalText, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidDataException(LocalizationService.Get("ConfigJsonEmpty"));
        foreach (var entry in file.Entries) SetJsonValue(root, entry);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + file.NewLine;
    }

    private static void SetJsonValue(JsonNode root, PluginConfigEntry entry)
    {
        var segments = entry.Locator.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(UnescapePointer).ToArray();
        if (segments.Length == 0) throw new InvalidDataException(LocalizationService.Get("ConfigJsonRootUnsupported"));
        JsonNode current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current switch
            {
                JsonObject obj => obj[segments[i]] ?? throw new InvalidDataException(LocalizationService.Get("ConfigJsonPathInvalid")),
                JsonArray array when int.TryParse(segments[i], out var index) && index >= 0 && index < array.Count => array[index] ?? throw new InvalidDataException(LocalizationService.Get("ConfigJsonPathInvalid")),
                _ => throw new InvalidDataException(LocalizationService.Get("ConfigJsonPathInvalid"))
            };
        }
        var replacement = CreateJsonValue(entry);
        var last = segments[^1];
        if (current is JsonObject parentObject) parentObject[last] = replacement;
        else if (current is JsonArray parentArray && int.TryParse(last, out var index) && index >= 0 && index < parentArray.Count) parentArray[index] = replacement;
        else throw new InvalidDataException(LocalizationService.Get("ConfigJsonPathInvalid"));
    }

    private static JsonNode? CreateJsonValue(PluginConfigEntry entry) => entry.ValueKind switch
    {
        PluginConfigValueKind.JsonBoolean => JsonValue.Create(bool.Parse(entry.Value)),
        PluginConfigValueKind.JsonInteger => JsonValue.Create(long.Parse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
        PluginConfigValueKind.JsonNumber => JsonValue.Create(double.Parse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
        PluginConfigValueKind.JsonNull => null,
        _ => JsonValue.Create(entry.Value)
    };

    private static void Validate(PluginConfigEntry entry)
    {
        if (entry.AllowedValues.Count > 0 && !entry.AllowedValues.Contains(entry.Value, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(LocalizationService.Format("ConfigAllowedValuesError", entry.Key, string.Join(", ", entry.AllowedValues)));
        var valid = entry.ValueKind switch
        {
            PluginConfigValueKind.Boolean or PluginConfigValueKind.JsonBoolean => bool.TryParse(entry.Value, out _),
            PluginConfigValueKind.Integer or PluginConfigValueKind.JsonInteger => long.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            PluginConfigValueKind.Number or PluginConfigValueKind.JsonNumber => double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
            PluginConfigValueKind.JsonNull => entry.Value.Equals("null", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        if (!valid) throw new InvalidDataException(LocalizationService.Format("ConfigValueInvalid", entry.Section, entry.Key, entry.TypeName));
        if (entry.MinimumValue.HasValue && entry.MaximumValue.HasValue &&
            double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue) &&
            (numericValue < entry.MinimumValue.Value || numericValue > entry.MaximumValue.Value))
            throw new InvalidDataException(LocalizationService.Format("ConfigRangeError", entry.Key, entry.MinimumValue.Value, entry.MaximumValue.Value));
    }

    private static PluginConfigValueKind InferCfgKind(string type, string value)
    {
        if (type.Contains("bool", StringComparison.OrdinalIgnoreCase) || bool.TryParse(value, out _)) return PluginConfigValueKind.Boolean;
        if (type.Contains("int", StringComparison.OrdinalIgnoreCase) || type.Contains("byte", StringComparison.OrdinalIgnoreCase)) return PluginConfigValueKind.Integer;
        if (type.Contains("single", StringComparison.OrdinalIgnoreCase) || type.Contains("double", StringComparison.OrdinalIgnoreCase) || type.Contains("decimal", StringComparison.OrdinalIgnoreCase)) return PluginConfigValueKind.Number;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return PluginConfigValueKind.Integer;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return PluginConfigValueKind.Number;
        return PluginConfigValueKind.String;
    }

    private static int MatchScore(string path, IReadOnlyList<string> identities, string guid)
    {
        var stem = Normalize(Path.GetFileNameWithoutExtension(path));
        var score = identities.Select(identity => stem == identity ? 95 : stem.Contains(identity, StringComparison.Ordinal) || identity.Contains(stem, StringComparison.Ordinal) ? 80 : 0).DefaultIfEmpty().Max();
        if (!path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) return score;
        try
        {
            var header = string.Join(' ', File.ReadLines(path).Take(12));
            var normalizedHeader = Normalize(header);
            if (!string.IsNullOrWhiteSpace(guid) && header.Contains(guid, StringComparison.OrdinalIgnoreCase)) return 110;
            if (identities.Any(identity => normalizedHeader.Contains(identity, StringComparison.Ordinal))) score = Math.Max(score, 100);
        }
        catch { /* Unreadable files are ignored by score. */ }
        return score;
    }

    private static void EnsureInsideConfigDirectory(string path, string gameRoot)
    {
        var root = Path.GetFullPath(Path.Combine(gameRoot, "BepInEx", "config")) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(LocalizationService.Get("ConfigPathUnsafe"));
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string EscapePointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string UnescapePointer(string value) => value.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}
