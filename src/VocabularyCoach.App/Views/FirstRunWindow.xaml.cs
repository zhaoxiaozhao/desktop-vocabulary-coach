using System.Windows;
using VocabularyCoach.App.ViewModels;

namespace VocabularyCoach.App.Views;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Finished += OnFinished;
    }

    /// <summary>用户是否选择了完成后立即弹卡。</summary>
    public bool ShowTestCard { get; private set; }

    private void OnFinished(object? sender, bool showTestCard)
    {
        ShowTestCard = showTestCard;
        DialogResult = true;
    }
}
