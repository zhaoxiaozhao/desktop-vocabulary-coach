namespace VocabularyCoach.Application.Settings;

/// <summary>
/// 浮动卡片的显示参数，绑定 appsettings.json 的 "Card" 节点。
/// Sprint 5 引入设置界面后，这里会改由设置服务提供并支持热更新。
/// </summary>
public sealed class CardDisplayOptions
{
    public const string SectionName = "Card";

    /// <summary>卡片宽度（DIP）。建议 340-400。</summary>
    public double Width { get; set; } = 380;

    /// <summary>距离屏幕工作区边缘的安全间距（DIP）。</summary>
    public double EdgeMargin { get; set; } = 24;

    /// <summary>自动关闭秒数；小于等于 0 表示不自动关闭。</summary>
    public int AutoCloseSeconds { get; set; } = 60;

    /// <summary>是否启用淡入淡出动画。</summary>
    public bool Animate { get; set; } = true;

    /// <summary>是否显示音标。</summary>
    public bool ShowPhonetic { get; set; } = true;

    /// <summary>是否在弹出时直接展开释义。</summary>
    public bool AutoExpand { get; set; } = false;

    /// <summary>卡片主题。</summary>
    public CardTheme Theme { get; set; } = CardTheme.Light;
}
