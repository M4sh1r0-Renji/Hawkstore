using Microsoft.Win32;
using Ravenhawk.Models;
using Ravenhawk.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Ravenhawk;

public partial class MainWindow : Window
{
    private const string DefaultGameRoot = @"D:\SteamLibrary\steamapps\common\Ravenfield";
    private readonly PluginService _pluginService = new();
    private readonly BepInExInstaller _installer = new();
    private readonly ObservableCollection<PluginItem> _plugins = new();
    private readonly List<PluginItem> _allPlugins = new();
    private PluginItem? _selected;

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
        GamePathBox.Text = FindInitialGameRoot();
        Loaded += (_, _) => RefreshPlugins();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        CardColumns = CalculateCardColumns(e.NewSize.Width);
    }

    public static int CalculateCardColumns(double width) => width < 900 ? 1 : width < 1500 ? 2 : 3;

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
            ApplyFilter();
            UpdateBepInExState();
            StatusText.Text = $"已扫描 {_allPlugins.Count} 个本地插件项目";
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
        PluginCountText.Text = string.IsNullOrWhiteSpace(query) ? $"{_allPlugins.Count} 项" : $"{_plugins.Count} / {_allPlugins.Count} 项";
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
        else if (File.Exists(core)) BepInExStateText.Text = "BepInEx 5 已安装";
        else BepInExStateText.Text = "未检测到 BepInEx 5";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 Ravenfield 游戏根目录", InitialDirectory = Directory.Exists(GamePathBox.Text) ? GamePathBox.Text : null };
        if (dialog.ShowDialog(this) == true) { GamePathBox.Text = dialog.FolderName; RefreshPlugins(); }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshPlugins();

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
        DetailsView.Visibility = Visibility.Visible;
        DetailTitleText.Text = item.DisplayName;
        DetailStateText.Text = item.StateText;
        DetailToggle.IsChecked = item.IsEnabled;
        NameBox.Text = _selected.Manifest.Name;
        AuthorBox.Text = _selected.Manifest.Author;
        VersionBox.Text = _selected.Manifest.Version;
        GameVersionBox.Text = _selected.Manifest.SupportedGameVersion;
        DescriptionBox.Text = _selected.Manifest.Description;
        UpdatedBox.Text = _selected.UpdatedText;
        EntryPathText.Text = _selected.FullPath;
        EntryPathText.ToolTip = _selected.FullPath;
    }

    private void ShowHome()
    {
        DetailsView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHome();
    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowHome();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        try
        {
            var manifest = new PluginManifest
            {
                Name = NameBox.Text.Trim(), Author = AuthorBox.Text.Trim(), Version = VersionBox.Text.Trim(),
                SupportedGameVersion = GameVersionBox.Text.Trim(), Description = DescriptionBox.Text.Trim()
            };
            _pluginService.SaveManifest(_selected, manifest);
            UpdatedBox.Text = _selected.UpdatedText;
            DetailTitleText.Text = _selected.DisplayName;
            PluginList.Items.Refresh();
            StatusText.Text = $"已保存 {_selected.DisplayName} 的详细信息";
        }
        catch (Exception ex) { ShowError(ex.Message); }
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
            var entryName = item.EntryName;
            var enabling = !item.IsEnabled;
            _pluginService.Toggle(item, GamePathBox.Text.Trim());
            RefreshPlugins();
            StatusText.Text = $"已{(enabling ? "启用" : "禁用")} {name}";
            if (reopenDetails)
            {
                var refreshed = _allPlugins.FirstOrDefault(x => x.EntryName.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                if (refreshed is not null) ShowDetails(refreshed);
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            var version = await _installer.InstallLatestV5Async(GamePathBox.Text.Trim(), progress);
            MessageBox.Show(this, $"BepInEx {version} 已安装到 Ravenfield。\n\n如果这是首次安装，请启动并退出一次游戏，让 BepInEx 生成配置文件。", "安装完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
}
