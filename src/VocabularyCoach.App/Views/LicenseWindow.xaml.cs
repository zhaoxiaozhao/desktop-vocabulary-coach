using System.Windows;

namespace VocabularyCoach.App.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow(ViewModels.LicenseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
