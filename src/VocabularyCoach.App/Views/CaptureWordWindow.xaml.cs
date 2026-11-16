using System.Windows;
using System.Windows.Input;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class CaptureWordWindow : Window
{
    public CaptureWordWindow(CaptureWordViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Loaded += (_, _) =>
        {
            WordBox.Focus();
            WordBox.SelectAll();
        };
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
