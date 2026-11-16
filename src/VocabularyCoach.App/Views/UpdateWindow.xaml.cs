using System.Windows;

namespace VocabularyCoach.App.Views;

public partial class UpdateWindow : Window
{
    private readonly ViewModels.UpdateViewModel _viewModel;

    public UpdateWindow(ViewModels.UpdateViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.CheckAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
