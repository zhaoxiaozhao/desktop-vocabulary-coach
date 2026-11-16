namespace VocabularyCoach.Application.Settings;

/// <summary>
/// 推荐策略参数，绑定 appsettings.json 的 "Recommendation" 节点。
/// 权重集中在这里，不散落在 UI 或仓储中。
/// </summary>
public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendation";

    /// <summary>复习压力较低时的新词目标比例（0–1）。</summary>
    public double NewWordRatio { get; set; } = 0.6;

    /// <summary>到期复习数量不超过该值时视为「压力较低」，可混合新词。</summary>
    public int ReviewPressureThreshold { get; set; } = 2;

    /// <summary>逾期超过该小时数时无条件优先复习，不再混合新词。</summary>
    public double OverdueGraceHours { get; set; } = 6;

    /// <summary>一次推荐最多读取的到期复习词数量。</summary>
    public int ReviewCandidateLimit { get; set; } = 20;
}
