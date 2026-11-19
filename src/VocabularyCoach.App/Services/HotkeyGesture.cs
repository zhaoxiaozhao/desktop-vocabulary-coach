using System.Windows.Input;

namespace VocabularyCoach.App.Services;

/// <summary>可注册到 RegisterHotKey 的组合键。</summary>
public readonly record struct HotkeyGesture(
    uint Modifiers,
    uint VirtualKey,
    string DisplayText)
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        var modifiers = 0u;
        Key? key = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Control;
                    break;
                case "alt":
                    modifiers |= Alt;
                    break;
                case "shift":
                    modifiers |= Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= Win;
                    break;
                default:
                    key = ParseKey(part);
                    break;
            }
        }

        if (modifiers == 0 || key is null)
        {
            return false;
        }

        return TryCreate(modifiers, key.Value, out gesture);
    }

    public static bool TryCreate(
        uint modifiers,
        Key key,
        out HotkeyGesture gesture)
    {
        gesture = default;

        if (modifiers == 0 || IsModifierKey(key) || key is Key.None)
        {
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(
            modifiers,
            (uint)virtualKey,
            Format(modifiers, key));
        return true;
    }

    public static bool TryCreate(
        ModifierKeys modifiers,
        Key key,
        out HotkeyGesture gesture)
    {
        var native = 0u;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            native |= Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            native |= Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            native |= Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            native |= Win;
        }

        return TryCreate(native, key, out gesture);
    }

    public static bool TryParseKey(string? token, out Key key)
    {
        key = ParseKey(token ?? string.Empty) ?? Key.None;
        return key is not Key.None;
    }

    private static Key? ParseKey(string token)
    {
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                return (Key)Enum.Parse(typeof(Key), ch.ToString());
            }

            if (ch is >= '0' and <= '9')
            {
                return (Key)((int)Key.D0 + (ch - '0'));
            }
        }

        var normalized = token switch
        {
            "Esc" => "Escape",
            "Return" => "Enter",
            "Spacebar" => "Space",
            _ => token,
        };

        return Enum.TryParse<Key>(normalized, ignoreCase: true, out var parsed)
               && parsed is not Key.None
            ? parsed
            : null;
    }

    private static string Format(uint modifiers, Key key)
    {
        var parts = new List<string>(4);

        if ((modifiers & Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & Win) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(DescribeKey(key));
        return string.Join('+', parts);
    }

    private static string DescribeKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)key - (int)Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0),
        Key.Space => "Space",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Return => "Enter",
        _ => key.ToString(),
    };

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;
}
