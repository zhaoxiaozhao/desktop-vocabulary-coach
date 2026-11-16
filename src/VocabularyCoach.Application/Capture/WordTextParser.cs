using System.Text.RegularExpressions;

namespace VocabularyCoach.Application.Capture;

/// <summary>解析后的单词或短语。</summary>
public sealed record ParsedWord(string Original, string Normalized);

/// <summary>
/// 捕获文本的清洗与校验：支持 1–5 个英文单词的短语，
/// 去除首尾标点和多余空白，保留内部连字符/撇号。
/// </summary>
public static class WordTextParser
{
    public const int MaxLength = 64;
    public const int MaxWordCount = 5;

    private static readonly char[] TrimChars =
    [
        '"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']', '{', '}', '<', '>',
        ',', '.', ';', ':', '!', '?', '，', '。', '；', '：', '！', '？', '、',
        '—', '–', '-', '/', '\\', '|', '_', '=', '+', '*', '~', '`',
        ' ', '\t', '\r', '\n',
    ];

    private static readonly Regex TokenPattern = new(
        "^[A-Za-z](?:[A-Za-z'’-]*[A-Za-z])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespacePattern = new(
        "\\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? rawText, out ParsedWord parsed, out string? error)
    {
        parsed = new ParsedWord(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(rawText))
        {
            error = "没有读取到文字，请先选中一个英文单词或短语。";
            return false;
        }

        var trimmed = rawText.Trim().Trim(TrimChars).Trim();
        if (trimmed.Length == 0)
        {
            error = "捕获内容为空，请重新选择英文单词或短语。";
            return false;
        }

        var collapsed = WhitespacePattern.Replace(trimmed, " ");

        if (collapsed.Length > MaxLength)
        {
            error = $"内容过长（最多 {MaxLength} 个字符）。";
            return false;
        }

        var tokens = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is 0 or > MaxWordCount)
        {
            error = $"只支持 1–{MaxWordCount} 个英文单词。";
            return false;
        }

        if (tokens.Any(token => !TokenPattern.IsMatch(token)))
        {
            error = "只支持英文单词或短语（可包含连字符和撇号）。";
            return false;
        }

        parsed = new ParsedWord(collapsed, collapsed.ToLowerInvariant());
        error = null;
        return true;
    }
}
