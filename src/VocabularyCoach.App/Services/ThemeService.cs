using System.Windows;
using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.App.Services;

/// <summary>
/// 主题切换：只替换颜色资源字典；样式中的画刷使用 DynamicResource，
/// 因此已打开的窗口/卡片也会即时更新，无需重建窗口。
/// </summary>
public sealed class ThemeService
{
    private static readonly Uri LightColors =
        new("pack://application:,,,/VocabularyCoach;component/Resources/Colors.Light.xaml");

    private static readonly Uri DarkColors =
        new("pack://application:,,,/VocabularyCoach;component/Resources/Colors.Dark.xaml");

    public CardTheme Current { get; private set; } = CardTheme.Light;

    public void Apply(CardTheme theme)
    {
        Current = theme;

        var merged = System.Windows.Application.Current?.Resources.MergedDictionaries;
        if (merged is null)
        {
            return;
        }

        var colors = new ResourceDictionary
        {
            Source = theme == CardTheme.Dark ? DarkColors : LightColors,
        };

        var existing = merged.FirstOrDefault(
            dictionary => dictionary.Source?.OriginalString.Contains("Colors.", StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            merged[merged.IndexOf(existing)] = colors;
        }
        else
        {
            merged.Insert(0, colors);
        }
    }
}
