using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VocabularyCoach.App.Services;

namespace VocabularyCoach.App.Controls;

/// <summary>
/// 点击后按下组合键即可录入快捷键，例如 Ctrl+Alt+W。
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(string),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnHotkeyChanged));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        ToolTip = "点击这里，然后按下新的组合键；至少包含 Ctrl、Alt、Shift 或 Win 之一。";
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private static void OnHotkeyChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyBox box)
        {
            box.Text = e.NewValue as string ?? string.Empty;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.None)
        {
            return;
        }

        if (!HotkeyGesture.TryCreate(Keyboard.Modifiers, key, out var gesture))
        {
            return;
        }

        Hotkey = gesture.DisplayText;
        SelectAll();
    }
}
