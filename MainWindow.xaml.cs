using Microsoft.Win32;
using Ravenhawk.Models;
using Ravenhawk.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Ravenhawk;

public enum PluginSortMode
{
    Updated,
    Name,
    Size
}

public partial class MainWindow : Window
{
    private const string DefaultGameRoot = @"D:\SteamLibrary\steamapps\common\Ravenfield";
    private readonly PluginService _pluginService = new();
    private readonly BepInExInstaller _installer = new();
    private readonly RegistryService _registryService = new();
    private readonly StoreInstaller _storeInstaller = new();
    private readonly ObservableCollection<PluginItem> _plugins = new();
    private readonly List<PluginItem> _allPlugins = new();
    private readonly ObservableCollection<StorePackageItem> _storePackages = new();
    private readonly List<StorePackageItem> _allStorePackages = new();
    private PluginItem? _selected;
    private bool _storeLoaded;
    private PluginSortMode _sortMode = PluginSortMode.Updated;

    public static readonly DependencyProperty CardColumnsProperty = DependencyProperty.Register(
        nameof(CardColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(2));
    public int CardColumns
    {
        get => (int)GetValue(CardColumnsProperty);
        set => SetValue(CardColumnsProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        PluginList.ItemsSource = _plugins;
        StoreList.ItemsSource = _storePackages;
        GamePathBox.Text = FindInitialGameRoot();
        Loaded += (_, _) => RefreshPlugins();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        CardColumns = CalculateCardColumns(e.NewSize.Width);
    }

    public static int CalculateCardColumns(double width) => width < 820 ? 1 : width < 1120 ? 2 : 3;

    private static string FindInitialGameRoot()
    {
        if (Directory.Exists(DefaultGameRoot)) return DefaultGameRoot;
        var common = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (!string.IsNullOrWhiteSpace(common))
        {
            var candidate = Path.Combine(common, "steamapps", "common", "Ravenfield");
            if (Directory.Exists(candidate)) return candidate;
        }
        return DefaultGameRoot;
    }

    private void RefreshPlugins()
    {
        try
        {
            _allPlugins.Clear();
            _allPlugins.AddRange(_pluginService.Scan(GamePathBox.Text.Trim()));
            SortPlugins();
            ApplyFilter();
            UpdateBepInExState();
            StatusText.Text = LocalizationService.Format("ScannedStatus", _allPlugins.Count);
            _selected = null;
            ShowHome();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ApplyFilter()
    {
        var query = SearchBox?.Text.Trim() ?? "";
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allPlugins
            : _allPlugins.Where(x =>
                x.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Manifest.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Manifest.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        _plugins.Clear();
        foreach (var plugin in matches) _plugins.Add(plugin);
        PluginCountText.Text = string.IsNullOrWhiteSpace(query)
            ? LocalizationService.Format("ItemCount", _allPlugins.Count)
            : LocalizationService.Format("FilteredCount", _plugins.Count, _allPlugins.Count);
        EmptyListText.Visibility = _plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginList.Visibility = _plugins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateBepInExState()
    {
        var root = GamePathBox.Text.Trim();
        var marker = Path.Combine(root, "BepInEx", "hawkstore-bepinex-version.txt");
        var legacyMarker = Path.Combine(root, "BepInEx", "ravenhawk-bepinex-version.txt");
        var core = Path.Combine(root, "BepInEx", "core", "BepInEx.dll");
        if (File.Exists(marker)) BepInExStateText.Text = $"BepInEx {File.ReadAllText(marker).Trim()}";
        else if (File.Exists(legacyMarker)) BepInExStateText.Text = $"BepInEx {File.ReadAllText(legacyMarker).Trim()}";
        else if (File.Exists(core)) BepInExStateText.Text = LocalizationService.Get("BepInExInstalled");
        else BepInExStateText.Text = LocalizationService.Get("BepInExMissing");
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = LocalizationService.Get("ChooseGameFolderTitle"), InitialDirectory = Directory.Exists(GamePathBox.Text) ? GamePathBox.Text : null };
        if (dialog.ShowDialog(this) == true) { GamePathBox.Text = dialog.FolderName; RefreshPlugins(); }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshPlugins();

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        if (SortButton.ContextMenu is null) return;
        SortButton.ContextMenu.PlacementTarget = SortButton;
        SortButton.ContextMenu.IsOpen = true;
    }

    private void SortOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse(tag, out PluginSortMode mode)) return;
        _sortMode = mode;
        SortPlugins();
        ApplyFilter();
        UpdateSortButtonText();
    }

    private void SortPlugins()
    {
        IOrderedEnumerable<PluginItem> sorted = _sortMode switch
        {
            PluginSortMode.Name => _allPlugins.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            PluginSortMode.Size => _allPlugins.OrderByDescending(x => x.SizeBytes),
            _ => _allPlugins.OrderByDescending(x => x.UpdatedAt)
        };
        var snapshot = sorted.ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        _allPlugins.Clear();
        _allPlugins.AddRange(snapshot);
    }

    private void UpdateSortButtonText()
    {
        SortButton.Content = LocalizationService.Get(_sortMode switch
        {
            PluginSortMode.Name => "SortNameButton",
            PluginSortMode.Size => "SortSizeButton",
            _ => "SortUpdatedButton"
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyFilter();
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        _selected = (sender as FrameworkElement)?.DataContext as PluginItem;
        if (_selected is null) return;
        ShowDetails(_selected);
    }

    private void ShowDetails(PluginItem item)
    {
        _selected = item;
        HomeView.Visibility = Visibility.Collapsed;
        StoreView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Visible;
        SetNavigation(homeActive: true);
        DetailTitleText.Text = item.DisplayName;
        DetailStateText.Text = item.StateText;
        DetailToggle.IsChecked = item.IsEnabled;
        NameBox.Text = item.DisplayName;
        AuthorBox.Text = item.AuthorText;
        VersionBox.Text = item.VersionText;
        GameVersionBox.Text = item.GameVersionText;
        DescriptionBox.Text = item.DescriptionText;
        UpdatedBox.Text = _selected.UpdatedText;
        SizeBox.Text = item.SizeText;
        SteamIdBox.Text = item.SteamIdText;
        SteamVerificationBox.Text = item.SteamVerificationText;
        EntryPathText.Text = _selected.FullPath;
        EntryPathText.ToolTip = _selected.FullPath;
    }

    private void ShowHome()
    {
        DetailsView.Visibility = Visibility.Collapsed;
        StoreView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
        SetNavigation(homeActive: true);
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowHome();

    private async void StoreButton_Click(object sender, RoutedEventArgs e)
    {
        ShowStore();
        if (!_storeLoaded) await RefreshStoreAsync();
    }

    private void ShowStore()
    {
        HomeView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Collapsed;
        StoreView.Visibility = Visibility.Visible;
        SetNavigation(homeActive: false);
    }

    private void SetNavigation(bool homeActive)
    {
        HomeButton.Style = (Style)FindResource(homeActive ? "ActiveNavButton" : "NavButton");
        StoreButton.Style = (Style)FindResource(homeActive ? "NavButton" : "ActiveNavButton");
    }

    private async void StoreRefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshStoreAsync();

    private async Task RefreshStoreAsync()
    {
        StoreRefreshButton.IsEnabled = false;
        try
        {
            StatusText.Text = LocalizationService.Get("RegistryLoading");
            var index = await _registryService.LoadIndexAsync();
            _allStorePackages.Clear();
            foreach (var entry in index.Packages.OrderByDescending(x => x.Featured).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _allStorePackages.Add(new StorePackageItem
                {
                    Entry = entry,
                    IsInstalled = IsStorePackageInstalled(entry.InstallDirectory)
                });
            }
            _storeLoaded = true;
            ApplyStoreFilter();
            StatusText.Text = LocalizationService.Format("StoreUpdated", _allStorePackages.Count);
        }
        catch (Exception ex) { ShowError(LocalizationService.Format("StoreLoadFailed", ex.Message)); }
        finally { StoreRefreshButton.IsEnabled = true; }
    }

    private void StoreSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        StoreSearchHint.Visibility = string.IsNullOrEmpty(StoreSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) ApplyStoreFilter();
    }

    private void ApplyStoreFilter()
    {
        var query = StoreSearchBox?.Text.Trim() ?? "";
        var matches = string.IsNullOrWhiteSpace(query)
            ? _allStorePackages
            : _allStorePackages.Where(x =>
                x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Author.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.CategoryText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        _storePackages.Clear();
        foreach (var package in matches) _storePackages.Add(package);
        StoreCountText.Text = string.IsNullOrWhiteSpace(query)
            ? LocalizationService.Format("ItemCount", _allStorePackages.Count)
            : LocalizationService.Format("FilteredCount", _storePackages.Count, _allStorePackages.Count);
        StoreEmptyText.Visibility = _storePackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StoreList.Visibility = _storePackages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool IsStorePackageInstalled(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || Path.GetFileName(installDirectory) != installDirectory) return false;
        var bepinExRoot = Path.Combine(GamePathBox.Text.Trim(), "BepInEx");
        return Directory.Exists(Path.Combine(bepinExRoot, "plugins", installDirectory)) ||
               Directory.Exists(Path.Combine(bepinExRoot, "plugins_disabled", installDirectory));
    }

    private async void InstallStoreButton_Click(object sender, RoutedEventArgs e)
    {
        var package = (sender as FrameworkElement)?.DataContext as StorePackageItem;
        if (package is null || package.IsInstalling) return;

        package.IsInstalling = true;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var manifest = await _registryService.LoadManifestAsync(package.Entry.ManifestUrl);
            if (!manifest.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Get("RegistryIdMismatch"));
            await _storeInstaller.InstallAsync(manifest, GamePathBox.Text.Trim(), progress);

            _allPlugins.Clear();
            _allPlugins.AddRange(_pluginService.Scan(GamePathBox.Text.Trim()));
            SortPlugins();
            ApplyFilter();
            UpdateBepInExState();
            package.IsInstalled = true;
            MessageBox.Show(this, LocalizationService.Format("PluginInstalledMessage", manifest.Name, manifest.Version), LocalizationService.Get("InstallCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { package.IsInstalling = false; }
    }

    private void CardToggle_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as PluginItem;
        if (item is null) return;
        TogglePlugin(item, reopenDetails: false);
        e.Handled = true;
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) TogglePlugin(_selected, reopenDetails: true);
        e.Handled = true;
    }

    private void TogglePlugin(PluginItem item, bool reopenDetails)
    {
        try
        {
            var name = item.DisplayName;
            var enabling = !item.IsEnabled;
            item.FullPath = _pluginService.Toggle(item, GamePathBox.Text.Trim());
            item.IsEnabled = enabling;
            item.RefreshBindings();
            StatusText.Text = LocalizationService.Format(enabling ? "PluginEnabledStatus" : "PluginDisabledStatus", name);
            if (reopenDetails) ShowDetails(item);
        }
        catch (Exception ex)
        {
            item.RefreshBindings();
            if (reopenDetails) DetailToggle.IsChecked = item.IsEnabled;
            ShowError(ex.Message);
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var version = await _installer.InstallLatestV5Async(GamePathBox.Text.Trim(), progress);
            MessageBox.Show(this, LocalizationService.Format("BepInExInstalledMessage", version), LocalizationService.Get("InstallCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshPlugins();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { InstallButton.IsEnabled = true; }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Hawkstore", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var next = LocalizationService.Current == AppLanguage.English
            ? AppLanguage.SimplifiedChinese
            : AppLanguage.English;
        LocalizationService.SetLanguage(next);
        foreach (var plugin in _allPlugins) plugin.RefreshBindings();
        foreach (var package in _allStorePackages) package.RefreshBindings();
        ApplyFilter();
        if (_storeLoaded) ApplyStoreFilter();
        UpdateBepInExState();
        UpdateSortButtonText();
        StatusText.Text = LocalizationService.Get("Ready");
        if (_selected is not null && DetailsView.Visibility == Visibility.Visible) ShowDetails(_selected);
    }
}
