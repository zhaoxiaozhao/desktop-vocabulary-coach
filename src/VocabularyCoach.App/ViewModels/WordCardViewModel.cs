using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.App.ViewModels;

/// <summary>选择释义模式下的单个选项。</summary>
public sealed partial class QuizChoiceViewModel : ObservableObject
{
    public QuizChoiceViewModel(string text, bool isCorrect, string label, bool isUnknown = false)
    {
        Text = text;
        IsCorrect = isCorrect;
        Label = label;
        IsUnknown = isUnknown;
    }

    public string Label { get; }

    public string Text { get; }

    public bool IsCorrect { get; }

    /// <summary>是否为固定的「不认识」选项。</summary>
    public bool IsUnknown { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isAnswered;
}

/// <summary>
/// 浮动卡片的视图模型。只负责展示数据与用户操作，
/// 不直接访问窗口或持久化层。
/// </summary>
public sealed partial class WordCardViewModel : ObservableObject
{
    private readonly WordCard _card;
    private readonly IAudioPronouncer _pronouncer;
    private readonly CardDisplayOptions _options;

    public WordCardViewModel(WordCard card, IAudioPronouncer pronouncer, CardDisplayOptions options)
    {
        _card = card;
        _pronouncer = pronouncer;
        _options = options;
        _isExpanded = options.AutoExpand;

        QuizChoices = card.Quiz is null
            ? []
            : BuildQuizChoices(card.Quiz);
    }

    private static IReadOnlyList<QuizChoiceViewModel> BuildQuizChoices(WordQuiz quiz)
    {
        var choices = quiz.Choices
            .Select((choice, index) => new QuizChoiceViewModel(
                choice.DefinitionZh,
                choice.IsCorrect,
                ((char)('A' + index)).ToString()))
            .ToList();

        // 固定追加「E：不认识」，让用户可以不猜直接标记。
        choices.Add(new QuizChoiceViewModel(
            "不认识",
            isCorrect: false,
            ((char)('A' + choices.Count)).ToString(),
            isUnknown: true));

        return choices;
    }

    /// <summary>卡片结果事件；窗口负责关闭并将结果回传给展示器。</summary>
    public event EventHandler<WordCardResult>? ResultSubmitted;

    public string Word => _card.Word;

    public string? Phonetic => _options.ShowPhonetic ? _card.DisplayPhonetic : null;

    public bool HasPhonetic => !string.IsNullOrWhiteSpace(Phonetic);

    public bool HasDetails => _card.HasDetails;

    public string? PartOfSpeech => _card.PrimaryDefinition?.PartOfSpeech;

    public bool HasPartOfSpeech => !string.IsNullOrWhiteSpace(PartOfSpeech);

    public string DefinitionZh => _card.PrimaryDefinition?.DefinitionZh ?? string.Empty;

    public string? ExampleEn => _card.PrimaryDefinition?.ExampleEn ?? _card.Enrichment?.ExampleEn;

    public string? ExampleZh => _card.PrimaryDefinition?.ExampleZh ?? _card.Enrichment?.ExampleZh;

    public bool HasExample => !string.IsNullOrWhiteSpace(ExampleEn);

    public bool ExampleFromAi =>
        string.IsNullOrWhiteSpace(_card.PrimaryDefinition?.ExampleEn) &&
        !string.IsNullOrWhiteSpace(_card.Enrichment?.ExampleEn);

    public string? AiExplanation => _card.Enrichment?.ExplanationZh;

    public bool HasAiExplanation => !string.IsNullOrWhiteSpace(AiExplanation);

    public string? ConfusableNote => _card.Enrichment?.ConfusableNote;

    public bool HasConfusableNote => !string.IsNullOrWhiteSpace(ConfusableNote);

    public bool HasAiNotes => HasAiExplanation || HasConfusableNote;

    /// <summary>是否为选择释义模式。</summary>
    public bool IsQuiz => _card.Quiz is not null;

    public IReadOnlyList<QuizChoiceViewModel> QuizChoices { get; }

    /// <summary>释义与例句是否可见：自我评估跟随展开状态；选择题在作答后揭示。</summary>
    public bool ShowDetails => IsQuiz ? IsAnswered : IsExpanded;

    public string CorrectDefinition => _card.PrimaryDefinition?.DefinitionZh ?? string.Empty;

    public string AnswerBanner => IsUnknownSelected
        ? "已标记为不认识，正确释义如下。"
        : LastAnswerCorrect
            ? "回答正确，下次复习间隔会拉长。"
            : "回答错误，正确释义如下。";

    public bool CanSpeak => _pronouncer.IsAvailable;

    /// <summary>音标行是否显示：有音标或可以发音时显示。</summary>
    public bool ShowPronunciationRow => HasPhonetic || CanSpeak;

    public string SnoozeLabel => "稍后提醒";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isAnswered;

    [ObservableProperty]
    private bool _lastAnswerCorrect;

    [ObservableProperty]
    private bool _isUnknownSelected;

    private WordCardResult? _pendingQuizResult;

    [ObservableProperty]
    private bool _isSpeaking;

    partial void OnIsAnsweredChanged(bool value) => OnPropertyChanged(nameof(ShowDetails));

    partial void OnLastAnswerCorrectChanged(bool value) => OnPropertyChanged(nameof(AnswerBanner));

    partial void OnIsUnknownSelectedChanged(bool value) => OnPropertyChanged(nameof(AnswerBanner));

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowDetails));

    [RelayCommand]
    private void ToggleExpand()
    {
        if (IsQuiz || !HasDetails)
        {
            return;
        }

        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void ChooseAnswer(QuizChoiceViewModel? choice)
    {
        if (!IsQuiz || IsAnswered || choice is null)
        {
            return;
        }

        foreach (var option in QuizChoices)
        {
            option.IsSelected = ReferenceEquals(option, choice);
            option.IsAnswered = true;
        }

        IsAnswered = true;
        IsUnknownSelected = choice.IsUnknown;
        LastAnswerCorrect = !choice.IsUnknown && choice.IsCorrect;
        _pendingQuizResult = choice.IsUnknown || !choice.IsCorrect
            ? WordCardResult.Unknown
            : WordCardResult.Recognized;
    }

    [RelayCommand]
    private void ContinueQuiz()
    {
        if (_pendingQuizResult is { } result)
        {
            Submit(result);
        }
    }

    [RelayCommand]
    private async Task SpeakAsync()
    {
        if (!CanSpeak || IsSpeaking)
        {
            return;
        }

        IsSpeaking = true;
        try
        {
            await _pronouncer.SpeakAsync(Word, CancellationToken.None);
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    [RelayCommand]
    private void MarkRecognized() => Submit(WordCardResult.Recognized);

    [RelayCommand]
    private void MarkVague() => Submit(WordCardResult.Vague);

    [RelayCommand]
    private void MarkUnknown() => Submit(WordCardResult.Unknown);

    [RelayCommand]
    private void Dismiss() => Submit(WordCardResult.Dismissed);

    [RelayCommand]
    private void Snooze() => Submit(WordCardResult.Snoozed);

    private void Submit(WordCardResult result)
    {
        ResultSubmitted?.Invoke(this, result);
    }
}
