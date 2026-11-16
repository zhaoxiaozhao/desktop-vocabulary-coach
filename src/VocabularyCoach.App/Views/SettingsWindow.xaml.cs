using System.Windows;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.CloseRequested += OnCloseRequested;
        viewModel.Saved += OnSaved;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.PendingApiKey = ApiKeyBox.Password;
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        ApiKeyBox.Clear();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
