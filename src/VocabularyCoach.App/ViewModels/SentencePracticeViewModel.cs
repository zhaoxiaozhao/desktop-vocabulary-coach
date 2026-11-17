using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.SentencePractice;

namespace VocabularyCoach.App.ViewModels;

/// <summary>句子填空练习窗口：选择或手输答案，提交后显示语法/固定句式说明。</summary>
public sealed partial class SentencePracticeViewModel : ObservableObject
{
    public const int SessionSize = 10;

    private readonly ISentencePracticeService _service;
    private readonly ILogger<SentencePracticeViewModel> _logger;

    private IReadOnlyList<SentencePracticeItem> _items = [];
    private int _index;
    private int _correctCount;
    private DateTimeOffset _shownAt;

    public SentencePracticeViewModel(
        ISentencePracticeService service,
        ILogger<SentencePracticeViewModel> logger)
    {
        _service = service;
        _logger = logger;
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<QuizChoiceViewModel> Options { get; } = [];

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _scoreText = string.Empty;

    [ObservableProperty]
    private string _sentenceText = string.Empty;

    [ObservableProperty]
    private string _translationText = string.Empty;

    [ObservableProperty]
    private string _patternText = string.Empty;

    [ObservableProperty]
    private string _grammarNote = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _correctAnswerText = string.Empty;

    [ObservableProperty]
    private string _typedAnswer = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTyping))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(CanSubmitTyping))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTyping))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    [NotifyPropertyChangedFor(nameof(CanSubmitTyping))]
    [NotifyPropertyChangedFor(nameof(ShowFooterNext))]
    private bool _isAnswered;

    [ObservableProperty]
    private bool _isCorrect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    [NotifyPropertyChangedFor(nameof(ShowFooterNext))]
    private bool _isFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTyping))]
    [NotifyPropertyChangedFor(nameof(CanSubmitTyping))]
    [NotifyPropertyChangedFor(nameof(TypingToggleText))]
    private bool _isTyping;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTyping))]
    [NotifyPropertyChangedFor(nameof(ShowResult))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowFooterNext))]
    private bool _hasItems;

    [ObservableProperty]
    private bool _showPattern;

    [ObservableProperty]
    private bool _showGrammarNote;

    public bool ShowOptions => HasItems && !IsTyping;

    public bool ShowTyping => HasItems && !IsAnswered && IsTyping;

    public bool ShowResult => HasItems && IsAnswered;

    public bool ShowFooterNext => HasItems && IsAnswered && !IsFinished;

    public bool ShowContent => HasItems && !IsLoading && !IsFinished;

    public bool ShowSummary => HasItems && IsFinished;

    public bool ShowLoading => IsLoading;

    public bool CanSubmitTyping =>
        IsTyping && !IsAnswered && !IsLoading && !string.IsNullOrWhiteSpace(TypedAnswer);

    public string TypingToggleText => IsTyping ? "改用选择" : "手动输入";

    partial void OnTypedAnswerChanged(string value) =>
        OnPropertyChanged(nameof(CanSubmitTyping));

    public async Task StartAsync()
    {
        IsLoading = true;
        IsFinished = false;
        IsAnswered = false;
        HasItems = false;
        _index = 0;
        _correctCount = 0;
        StatusMessage = string.Empty;
        SummaryText = string.Empty;
        Options.Clear();

        try
        {
            var session = await _service.CreateSessionAsync(SessionSize);
            _items = session.Items;
            StatusMessage = session.AiMessage ?? string.Empty;

            if (_items.Count == 0)
            {
                StatusMessage = "暂无可用句子。请确认 Seed/sentences.seed.json 存在，或在设置中启用 AI。";
                return;
            }

            HasItems = true;
            ShowCurrent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建句子练习失败。");
            StatusMessage = "加载句子失败，请稍后重试。";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ShowCurrent()
    {
        if (_index < 0 || _index >= _items.Count)
        {
            return;
        }

        var item = _items[_index];
        SentenceText = item.Sentence;
        TranslationText = item.TranslationZh;
        PatternText = item.Pattern ?? string.Empty;
        ShowPattern = false;
        GrammarNote = item.GrammarNote ?? string.Empty;
        ShowGrammarNote = false;
        SourceText = (item.FromAi ? "AI 生成例句" : "离线句库")
            + (item.WordId <= 0 ? " · 高频词，不计入复习" : string.Empty);
        ProgressText = $"第 {_index + 1} / {_items.Count} 句";
        ScoreText = $"已答对 {_correctCount} 句";
        ResultText = string.Empty;
        CorrectAnswerText = string.Empty;
        TypedAnswer = string.Empty;
        IsAnswered = false;
        IsCorrect = false;
        _shownAt = DateTimeOffset.UtcNow;

        Options.Clear();
        for (var i = 0; i < item.Options.Count; i++)
        {
            var text = item.Options[i];
            Options.Add(new QuizChoiceViewModel(
                text,
                string.Equals(text, item.Answer, StringComparison.OrdinalIgnoreCase),
                ((char)('A' + i)).ToString()));
        }

        Options.Add(new QuizChoiceViewModel(
            "不会",
            isCorrect: false,
            ((char)('A' + Options.Count)).ToString(),
            isUnknown: true));

        OnPropertyChanged(nameof(CanSubmitTyping));
    }

    [RelayCommand]
    private async Task ChooseOptionAsync(QuizChoiceViewModel? option)
    {
        if (IsAnswered || !ShowOptions || option is null)
        {
            return;
        }

        foreach (var item in Options)
        {
            item.IsSelected = ReferenceEquals(item, option);
            item.IsAnswered = true;
        }

        await EvaluateAsync(option.IsUnknown ? string.Empty : option.Text);
    }

    [RelayCommand(CanExecute = nameof(CanSubmitTyping))]
    private async Task SubmitTypingAsync()
    {
        await EvaluateAsync(TypedAnswer);
    }

    [RelayCommand]
    private async Task ShowAnswerAsync()
    {
        if (IsAnswered)
        {
            return;
        }

        foreach (var item in Options)
        {
            item.IsAnswered = true;
        }

        await EvaluateAsync(string.Empty);
    }

    private async Task EvaluateAsync(string answer)
    {
        if (IsAnswered || _index < 0 || _index >= _items.Count)
        {
            return;
        }

        var item = _items[_index];
        IsAnswered = true;

        try
        {
            var outcome = await _service.SubmitAsync(item, answer, _shownAt);
            IsCorrect = outcome.IsCorrect;
            if (outcome.IsCorrect)
            {
                _correctCount++;
            }

            ResultText = outcome.IsCorrect ? "回答正确" : "回答错误";
            CorrectAnswerText = item.Answer;
            ScoreText = $"已答对 {_correctCount} 句";
            ShowPattern = !string.IsNullOrWhiteSpace(PatternText);
            ShowGrammarNote = !string.IsNullOrWhiteSpace(GrammarNote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提交句子练习失败：{Word}", item.Word);
            ResultText = "已提交（保存失败，详见日志）";
            CorrectAnswerText = item.Answer;
        }

        foreach (var option in Options)
        {
            option.IsAnswered = true;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (!IsAnswered)
        {
            return;
        }

        if (_index + 1 >= _items.Count)
        {
            IsFinished = true;
            SummaryText = $"本组完成：{_items.Count} 句，答对 {_correctCount} 句。";
            ScoreText = $"答对 {_correctCount} / {_items.Count}";
            return;
        }

        _index++;
        ShowCurrent();
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await StartAsync();
    }

    [RelayCommand]
    private void ToggleTyping() => IsTyping = !IsTyping;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
