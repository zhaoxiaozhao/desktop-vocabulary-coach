namespace VocabularyCoach.Core.ValueObjects;

/// <summary>
/// 勿扰时段（本地时间）。支持跨零点，例如 22:30–08:00。
/// 起止时间相同表示禁用勿扰。
/// </summary>
public readonly record struct QuietHours(TimeOnly Start, TimeOnly End)
{
    public bool IsEnabled => Start != End;

    public static QuietHours Disabled => new(TimeOnly.MinValue, TimeOnly.MinValue);

    /// <summary>判断本地时间是否处于勿扰时段（含开始时刻，不含结束时刻）。</summary>
    public bool IsQuiet(TimeOnly localTime)
    {
        if (!IsEnabled)
        {
            return false;
        }

        return Start < End
            ? localTime >= Start && localTime < End
            : localTime >= Start || localTime < End;
    }

    /// <summary>解析 "HH:mm" 格式。任一格式非法时返回 false。</summary>
    public static bool TryParse(string? start, string? end, out QuietHours quietHours)
    {
        quietHours = Disabled;

        if (!TimeOnly.TryParse(start, out var startTime) || !TimeOnly.TryParse(end, out var endTime))
        {
            return false;
        }

        quietHours = new QuietHours(startTime, endTime);
        return true;
    }
}
