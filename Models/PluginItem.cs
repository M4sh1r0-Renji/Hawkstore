using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ravenhawk.Models;

public sealed class PluginItem : INotifyPropertyChanged
{
    private PluginManifest _manifest = new();

    public required string FullPath { get; set; }
    public required string EntryName { get; set; }
    public required bool IsDirectory { get; set; }
    public required bool IsEnabled { get; set; }
    public string Kind => IsDirectory ? "插件文件夹" : "DLL 插件";
    public PluginManifest Manifest
    {
        get => _manifest;
        set { _manifest = value; OnPropertyChanged(); }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name) ? Path.GetFileNameWithoutExtension(EntryName) : Manifest.Name;
    public string Summary => $"v{Manifest.Version}  ·  {Manifest.Author}";
    public string StateText => IsEnabled ? "已启用" : "已禁用";
    public string UpdatedText => Manifest.LastUpdated?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "未知";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshBindings()
    {
        OnPropertyChanged(nameof(Manifest));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(UpdatedText));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
