using System.Windows;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class StatisticsWindow : Window
{
    private readonly StatisticsViewModel _viewModel;

    public StatisticsWindow(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>每次打开时重新加载统计。</summary>
    public void Refresh() => _ = _viewModel.RefreshAsync();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
