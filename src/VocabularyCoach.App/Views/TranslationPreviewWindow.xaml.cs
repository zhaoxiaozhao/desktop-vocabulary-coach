using System.Windows;
using System.Windows.Input;

namespace VocabularyCoach.App.Views;

public partial class TranslationPreviewWindow : Window
{
    public TranslationPreviewWindow(string original, string translation)
    {
        InitializeComponent();
        OriginalBox.Text = original;
        TranslationBox.Text = translation;

        Loaded += (_, _) =>
        {
            TranslationBox.Focus();
            TranslationBox.SelectAll();
        };
    }

    public event Action<string>? ReplaceRequested;

    public event Action<string>? CopyRequested;

    public void SetStatus(string message) => StatusText.Text = message;

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        var text = TranslationBox.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("译文为空，请先补充英文。");
            return;
        }

        ReplaceRequested?.Invoke(text);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var text = TranslationBox.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("译文为空，请先补充英文。");
            return;
        }

        CopyRequested?.Invoke(text);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
