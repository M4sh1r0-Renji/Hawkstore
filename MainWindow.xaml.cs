using Microsoft.Win32;
using Ravenhawk.Models;
using Ravenhawk.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly PluginConfigService _configService = new();
    private readonly PublisherProjectStore _publisherStore = new();
    private readonly SteamIdentityService _steamIdentityService = new();
    private readonly GitHubPublisherService _githubPublisher = new();
    private readonly ObservableCollection<PluginItem> _plugins = new();
    private readonly List<PluginItem> _allPlugins = new();
    private readonly ObservableCollection<StorePackageItem> _storePackages = new();
    private readonly List<StorePackageItem> _allStorePackages = new();
    private readonly ObservableCollection<PluginConfigEntry> _configEntries = new();
    private readonly List<PluginConfigFile> _configFiles = new();
    private readonly ObservableCollection<PublisherProject> _publisherProjects = new();
    private PluginItem? _selected;
    private PluginConfigFile? _selectedConfig;
    private bool _storeLoaded;
    private PluginSortMode _sortMode = PluginSortMode.Updated;
    private PublisherProject? _publisherProject;
    private bool _publisherBusy;

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
        ConfigEntryList.ItemsSource = _configEntries;
        foreach (var project in _publisherStore.Load()) _publisherProjects.Add(project);
        PublisherProjectBox.ItemsSource = _publisherProjects;
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
        PublisherView.Visibility = Visibility.Collapsed;
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
        LoadPluginConfigs(item);
    }

    private void LoadPluginConfigs(PluginItem item)
    {
        _selectedConfig = null;
        _configEntries.Clear();
        _configFiles.Clear();
        _configFiles.AddRange(_configService.FindForPlugin(GamePathBox.Text.Trim(), item));
        ConfigFileBox.ItemsSource = null;
        ConfigFileBox.ItemsSource = _configFiles;
        ConfigEmptyText.Visibility = _configFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConfigEntryList.Visibility = Visibility.Collapsed;
        ConfigPathText.Text = "";
        SaveConfigButton.IsEnabled = false;
        if (_configFiles.Count > 0) ConfigFileBox.SelectedIndex = 0;
    }

    private void ConfigFileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigFileBox.SelectedItem is not PluginConfigFile descriptor) return;
        LoadConfigFile(descriptor.FullPath);
    }

    private void LoadConfigFile(string path)
    {
        try
        {
            _selectedConfig = _configService.Load(path);
            _configEntries.Clear();
            foreach (var entry in _selectedConfig.Entries) _configEntries.Add(entry);
            ConfigPathText.Text = _selectedConfig.FullPath;
            ConfigPathText.ToolTip = _selectedConfig.FullPath;
            ConfigEmptyText.Visibility = _configEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfigEntryList.Visibility = _configEntries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SaveConfigButton.IsEnabled = _configEntries.Count > 0;
            StatusText.Text = LocalizationService.Format("ConfigLoaded", _configEntries.Count, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _selectedConfig = null;
            _configEntries.Clear();
            ConfigEntryList.Visibility = Visibility.Collapsed;
            ConfigEmptyText.Visibility = Visibility.Visible;
            SaveConfigButton.IsEnabled = false;
            ShowError(LocalizationService.Format("ConfigLoadFailed", ex.Message));
        }
    }

    private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig is not null) LoadConfigFile(_selectedConfig.FullPath);
    }

    private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig is null) return;
        try
        {
            var fileName = Path.GetFileName(_selectedConfig.FullPath);
            var path = _selectedConfig.FullPath;
            var backup = _configService.Save(_selectedConfig, GamePathBox.Text.Trim());
            LoadConfigFile(path);
            StatusText.Text = LocalizationService.Format("ConfigSaved", fileName, backup);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowHome()
    {
        DetailsView.Visibility = Visibility.Collapsed;
        StoreView.Visibility = Visibility.Collapsed;
        PublisherView.Visibility = Visibility.Collapsed;
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
        PublisherView.Visibility = Visibility.Collapsed;
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
        ConfigEntryList.Items.Refresh();
        ApplyFilter();
        if (_storeLoaded) ApplyStoreFilter();
        UpdateBepInExState();
        UpdateSortButtonText();
        StatusText.Text = LocalizationService.Get("Ready");
        if (_selected is not null && DetailsView.Visibility == Visibility.Visible) ShowDetails(_selected);
        if (PublisherView.Visibility == Visibility.Visible) BindPublisherProject(_publisherProject);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Process.GetProcessesByName("ravenfield").Length > 0)
            {
                StatusText.Text = LocalizationService.Get("GameAlreadyRunning");
                return;
            }

            var root = GamePathBox.Text.Trim();
            var executable = Path.Combine(root, "Ravenfield.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException(LocalizationService.Get("GameExecutableMissing"), executable);
            Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = true });
            StatusText.Text = LocalizationService.Get("GameStarted");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void PublishBubbleButton_Click(object sender, RoutedEventArgs e) => ShowPublisher();
    private void PublisherBackButton_Click(object sender, RoutedEventArgs e) => ShowStore();

    private void ShowPublisher()
    {
        HomeView.Visibility = Visibility.Collapsed;
        StoreView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Collapsed;
        PublisherView.Visibility = Visibility.Visible;
        SetNavigation(homeActive: false);
        if (_publisherProjects.Count == 0) CreatePublisherProject();
        else if (_publisherProject is null) PublisherProjectBox.SelectedIndex = 0;
        else BindPublisherProject(_publisherProject);
    }

    private void CreatePublisherProject()
    {
        var identity = _steamIdentityService.FindCurrent();
        var project = new PublisherProject
        {
            AuthorName = identity?.PersonaName ?? "",
            SteamId = identity?.SteamId ?? ""
        };
        _publisherProjects.Add(project);
        PublisherProjectBox.SelectedItem = project;
        SavePublisherProjects();
    }

    private void PublisherProjectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _publisherProject = PublisherProjectBox.SelectedItem as PublisherProject;
        BindPublisherProject(_publisherProject);
    }

    private void NewPublisherProjectButton_Click(object sender, RoutedEventArgs e) => CreatePublisherProject();

    private void BindPublisherProject(PublisherProject? project)
    {
        if (project is null) return;
        _publisherProject = project;
        PublisherIdBox.Text = project.Id;
        PublisherNameBox.Text = project.Name;
        PublisherVersionBox.Text = project.Version;
        PublisherAuthorBox.Text = project.AuthorName;
        PublisherSteamIdBox.Text = project.SteamId;
        VerifySteamButton.Content = LocalizationService.Get(project.SteamVerified ? "SteamVerified" : "VerifySteam");
        PublisherGameVersionBox.Text = project.GameVersion;
        PublisherCategoriesBox.Text = project.Categories;
        PublisherGuidBox.Text = project.PluginGuid;
        PublisherInstallDirectoryBox.Text = project.InstallDirectory;
        PublisherEntryDllBox.Text = project.EntryDll;
        PublisherDescriptionBox.Text = project.Description;
        PublisherSourceFileBox.Text = project.SourceFile;
        PublisherInfoText.Text = project.Published
            ? $"https://github.com/{project.RepositoryFullName}"
            : LocalizationService.Get("PublisherSecurityNote");
        GitHubAccountText.Text = _githubPublisher.IsConnected
            ? LocalizationService.Format("GitHubConnected", _githubPublisher.Login)
            : LocalizationService.Get("GitHubNotConnected");
        UpdatePublisherButtons();
    }

    private void ReadPublisherForm(PublisherProject project)
    {
        project.Id = PublisherIdBox.Text.Trim();
        project.Name = PublisherNameBox.Text.Trim();
        project.Version = PublisherVersionBox.Text.Trim();
        project.AuthorName = PublisherAuthorBox.Text.Trim();
        project.SteamId = PublisherSteamIdBox.Text.Trim();
        project.GameVersion = PublisherGameVersionBox.Text.Trim();
        project.Categories = PublisherCategoriesBox.Text.Trim();
        project.PluginGuid = PublisherGuidBox.Text.Trim();
        project.InstallDirectory = PublisherInstallDirectoryBox.Text.Trim();
        project.EntryDll = PublisherEntryDllBox.Text.Trim();
        project.Description = PublisherDescriptionBox.Text.Trim();
        project.SourceFile = PublisherSourceFileBox.Text.Trim();
    }

    private void SavePublisherProjects()
    {
        _publisherStore.Save(_publisherProjects);
        PublisherProjectBox.Items.Refresh();
    }

    private void SavePublisherDraftButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherProject is null) return;
        ReadPublisherForm(_publisherProject);
        SavePublisherProjects();
        StatusText.Text = LocalizationService.Get("DraftSaved");
    }

    private void PublisherBrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("ChooseFile"),
            Filter = "Plugin packages (*.zip;*.dll)|*.zip;*.dll|ZIP files (*.zip)|*.zip|DLL files (*.dll)|*.dll"
        };
        if (dialog.ShowDialog(this) == true) PublisherSourceFileBox.Text = dialog.FileName;
    }

    private async void ConnectGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherBusy) return;
        SetPublisherBusy(true);
        try
        {
            StatusText.Text = LocalizationService.Get("PublisherConnecting");
            await _githubPublisher.ConnectAsync(info => Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(info.UserCode);
                PublisherInfoText.Text = LocalizationService.Format("PublisherDeviceCode", info.UserCode, info.VerificationUri);
            }));
            GitHubAccountText.Text = LocalizationService.Format("GitHubConnected", _githubPublisher.Login);
            StatusText.Text = GitHubAccountText.Text;
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetPublisherBusy(false); }
    }

    private async void VerifySteamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherProject is null || _publisherBusy) return;
        SetPublisherBusy(true);
        try
        {
            StatusText.Text = LocalizationService.Get("SteamVerificationWaiting");
            var identity = await _steamIdentityService.VerifyWithSteamAsync();
            _publisherProject.SteamId = identity.SteamId;
            _publisherProject.SteamVerified = true;
            _publisherProject.SteamVerifiedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(identity.PersonaName)) _publisherProject.AuthorName = identity.PersonaName;
            SavePublisherProjects();
            BindPublisherProject(_publisherProject);
            StatusText.Text = LocalizationService.Get("SteamVerifiedStatus");
        }
        catch (OperationCanceledException) { ShowError(LocalizationService.Get("SteamVerificationTimeout")); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetPublisherBusy(false); }
    }

    private async void PublishModButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherProject is null || _publisherBusy) return;
        ReadPublisherForm(_publisherProject);
        SavePublisherProjects();
        SetPublisherBusy(true);
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var result = await _githubPublisher.PublishAsync(_publisherProject, progress);
            SavePublisherProjects();
            BindPublisherProject(_publisherProject);
            StatusText.Text = LocalizationService.Format("PublisherPublished", result.SubmissionUrl);
            PublisherInfoText.Text = StatusText.Text;
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetPublisherBusy(false); }
    }

    private async void UpdateModButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherProject is null || _publisherBusy) return;
        ReadPublisherForm(_publisherProject);
        SetPublisherBusy(true);
        try
        {
            await _githubPublisher.UpdateMetadataAsync(_publisherProject);
            SavePublisherProjects();
            BindPublisherProject(_publisherProject);
            StatusText.Text = LocalizationService.Get("PublisherUpdated");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetPublisherBusy(false); }
    }

    private async void DeleteModButton_Click(object sender, RoutedEventArgs e)
    {
        if (_publisherProject is null || _publisherBusy) return;
        if (MessageBox.Show(this, LocalizationService.Get("PublisherDeleteConfirm"), LocalizationService.Get("PublisherDeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var deleting = _publisherProject;
        SetPublisherBusy(true);
        try
        {
            if (deleting.Published) await _githubPublisher.UnpublishAsync(deleting);
            _publisherProjects.Remove(deleting);
            _publisherProject = null;
            SavePublisherProjects();
            if (_publisherProjects.Count == 0) CreatePublisherProject();
            else PublisherProjectBox.SelectedIndex = 0;
            StatusText.Text = LocalizationService.Get("PublisherDeleted");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetPublisherBusy(false); }
    }

    private void SetPublisherBusy(bool busy)
    {
        _publisherBusy = busy;
        ConnectGitHubButton.IsEnabled = !busy;
        VerifySteamButton.IsEnabled = !busy;
        PublisherProjectBox.IsEnabled = !busy;
        UpdatePublisherButtons();
    }

    private void UpdatePublisherButtons()
    {
        var hasProject = _publisherProject is not null;
        var published = _publisherProject?.Published == true;
        PublishModButton.IsEnabled = !_publisherBusy && hasProject && !published && _githubPublisher.IsConnected && _publisherProject?.SteamVerified == true;
        UpdateModButton.IsEnabled = !_publisherBusy && published && _githubPublisher.IsConnected;
        DeleteModButton.IsEnabled = !_publisherBusy && hasProject && (!published || _githubPublisher.IsConnected);
    }
}
