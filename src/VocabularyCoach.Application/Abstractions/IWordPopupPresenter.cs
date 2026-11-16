using VocabularyCoach.Core.Entities;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Abstractions;

/// <summary>
/// 卡片展示抽象。后台服务通过该接口请求 UI 线程显示卡片，
/// 不直接接触任何 WPF 控件。
/// </summary>
/// <remarks>
/// 设计文档给出的签名是 <c>Task ShowAsync(...)</c>。这里改为返回
/// <see cref="WordCardResult"/>，因为卡片是唯一的用户反馈来源，
/// 直接回传结果可以避免额外的事件通道，语义也更清晰。
/// </remarks>
public interface IWordPopupPresenter
{
    /// <summary>显示一张卡片，直到用户反馈、关闭、稍后提醒或超时。</summary>
    Task<WordCardResult> ShowAsync(WordCard card, CancellationToken cancellationToken);

    /// <summary>关闭当前卡片（应用退出或调度取消时调用）。</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}
