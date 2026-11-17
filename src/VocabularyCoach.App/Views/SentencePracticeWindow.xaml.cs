using System.Windows;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class SentencePracticeWindow : Window
{
    private readonly SentencePracticeViewModel _viewModel;

    public SentencePracticeWindow(SentencePracticeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnLoaded;
        _viewModel.CloseRequested += OnCloseRequested;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAsync();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        Closed -= OnClosed;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
