using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ravenhawk.Services;

namespace Ravenhawk.Models;

public enum PluginConfigFormat
{
    BepInExCfg,
    Json
}

public enum PluginConfigValueKind
{
    String,
    Boolean,
    Integer,
    Number,
    JsonString,
    JsonBoolean,
    JsonInteger,
    JsonNumber,
    JsonNull
}

public sealed class PluginConfigFile
{
    public required string FullPath { get; init; }
    public required PluginConfigFormat Format { get; init; }
    public string DisplayName => Path.GetFileName(FullPath);
    public string OriginalText { get; set; } = "";
    public string NewLine { get; set; } = Environment.NewLine;
    public bool HasUtf8Bom { get; set; }
    public List<string> Lines { get; set; } = [];
    public List<PluginConfigEntry> Entries { get; set; } = [];
}

public sealed class PluginConfigEntry : INotifyPropertyChanged
{
    private string _value = "";

    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public string Description { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string DefaultValue { get; init; } = "";
    public string Locator { get; init; } = "";
    public int LineIndex { get; init; } = -1;
    public PluginConfigValueKind ValueKind { get; init; }
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
    public double? MinimumValue { get; init; }
    public double? MaximumValue { get; init; }
    public IReadOnlyList<string> EditorOptions => AllowedValues.Count > 0
        ? AllowedValues
        : ValueKind is PluginConfigValueKind.Boolean or PluginConfigValueKind.JsonBoolean ? ["true", "false"] : [];
    public bool HasEditorOptions => EditorOptions.Count > 0;
    public string MetadataText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TypeName)) parts.Add(TypeName);
            if (!string.IsNullOrWhiteSpace(DefaultValue)) parts.Add(LocalizationService.Format("ConfigDefaultFormat", DefaultValue));
            if (MinimumValue.HasValue && MaximumValue.HasValue)
                parts.Add(LocalizationService.Format("ConfigRangeFormat", MinimumValue.Value, MaximumValue.Value));
            if (AllowedValues.Count is > 0 and <= 8) parts.Add(string.Join(", ", AllowedValues));
            else if (AllowedValues.Count > 8) parts.Add(LocalizationService.Format("ConfigOptionsCount", AllowedValues.Count));
            return string.Join("  ·  ", parts);
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
