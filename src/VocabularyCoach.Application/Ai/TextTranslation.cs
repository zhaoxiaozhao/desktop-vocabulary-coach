namespace VocabularyCoach.Application.Ai;

/// <summary>中文转英文的结果；失败时 Message 说明原因。</summary>
public sealed record TextTranslationResult(bool Success, string Translation, string Message)
{
    public static TextTranslationResult Ok(string translation) =>
        new(true, translation, "翻译完成。");

    public static TextTranslationResult Fail(string message) =>
        new(false, string.Empty, message);
}

/// <summary>把一段中文翻译成自然英文；只发送当前选中的文字。</summary>
public interface ITextTranslationService
{
    Task<TextTranslationResult> TranslateAsync(
        string chineseText,
        CancellationToken cancellationToken = default);
}

/// <summary>译文清理与中文检测。</summary>
public static class TextTranslationText
{
    public static bool ContainsChinese(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if ((ch >= '\u4E00' && ch <= '\u9FFF')
                || (ch >= '\u3400' && ch <= '\u4DBF')
                || (ch >= '\uF900' && ch <= '\uFAFF'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>去掉模型常见的包裹：代码块、引号、前缀标签。</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                text = text[(firstLineEnd + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }
        }

        text = text.Trim();

        foreach (var prefix in new[] { "英文：", "英文:", "翻译：", "翻译:", "Translation:", "English:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"')
                || (text[0] == '“' && text[^1] == '”')
                || (text[0] == '\'' && text[^1] == '\'')))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }
}
