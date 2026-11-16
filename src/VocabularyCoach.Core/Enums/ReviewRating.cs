namespace VocabularyCoach.Core.Enums;

/// <summary>
/// 复习算法内部评分。与用户反馈一一对应：
/// 认识 → Good，模糊 → Hard，不认识 → Again。
/// </summary>
public enum ReviewRating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4,
}
