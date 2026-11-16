using System.Windows;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class CapturedWordsWindow : Window
{
    private readonly CapturedWordsViewModel _viewModel;

    public CapturedWordsWindow(CapturedWordsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public void Refresh() => _ = _viewModel.RefreshAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
