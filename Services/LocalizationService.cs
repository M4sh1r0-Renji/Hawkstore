using System.Windows;

namespace Ravenhawk.Services;

public enum AppLanguage
{
    English,
    SimplifiedChinese
}

public static class LocalizationService
{
    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static void SetLanguage(AppLanguage language)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(x =>
            x.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (current is not null) dictionaries.Remove(current);

        var file = language == AppLanguage.English ? "Strings.en.xaml" : "Strings.zh-CN.xaml";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Hawkstore;component/Resources/{file}", UriKind.RelativeOrAbsolute)
        });
        Current = language;
    }

    public static string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
