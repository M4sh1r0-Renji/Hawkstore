using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ravenhawk.Services;

namespace Ravenhawk.Models;

public sealed class PluginItem : INotifyPropertyChanged
{
    private PluginManifest _manifest = new();

    private string _fullPath = "";
    private bool _isEnabled;

    public required string FullPath
    {
        get => _fullPath;
        set { _fullPath = value; OnPropertyChanged(); }
    }
    public required string EntryName { get; set; }
    public required bool IsDirectory { get; set; }
    public required bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }
    public long SizeBytes { get; set; }
    public DateTimeOffset UpdatedAt => Manifest.LastUpdated ?? DateTimeOffset.MinValue;
    public string Kind => LocalizationService.Get(IsDirectory ? "KindFolder" : "KindDll");
    public PluginManifest Manifest
    {
        get => _manifest;
        set { _manifest = value; OnPropertyChanged(); }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.Name) ? Path.GetFileNameWithoutExtension(EntryName) : Manifest.Name;
    public string AuthorText => IsPlaceholder(Manifest.Author) ? LocalizationService.Get("UnknownAuthor") : Manifest.Author;
    public string VersionText => IsPlaceholder(Manifest.Version) ? LocalizationService.Get("Unknown") : Manifest.Version;
    public string GameVersionText => IsPlaceholder(Manifest.SupportedGameVersion) ? LocalizationService.Get("NotProvided") : Manifest.SupportedGameVersion;
    public string DescriptionText => string.IsNullOrWhiteSpace(Manifest.Description) || Manifest.Description == "暂无说明" ? LocalizationService.Get("NoDescription") : Manifest.Description;
    public string Summary => $"v{VersionText}  ·  {AuthorText}";
    public string StateText => LocalizationService.Get(IsEnabled ? "StateEnabled" : "StateDisabled");
    public string UpdatedText => Manifest.LastUpdated?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? LocalizationService.Get("Unknown");
    public string SizeText => FormatSize(SizeBytes);
    public string SteamIdText => string.IsNullOrWhiteSpace(Manifest.AuthorSteamId) ? LocalizationService.Get("NotProvided") : Manifest.AuthorSteamId;
    public string SteamVerificationText => string.IsNullOrWhiteSpace(Manifest.AuthorSteamId)
        ? LocalizationService.Get("IdentityUnavailable")
        : LocalizationService.Get(Manifest.AuthorSteamVerified ? "IdentityVerified" : "IdentityUnverified");

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshBindings()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(Manifest));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AuthorText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(GameVersionText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(SteamVerificationText));
        OnPropertyChanged(nameof(Kind));
    }

    private static bool IsPlaceholder(string value) => string.IsNullOrWhiteSpace(value) || value is "未知" or "未知作者" or "未填写";

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
