using Volumy.Localization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Volumy.Interop;

/// <summary>
/// A shortcut field you don't type into: click it and press the keys (e.g. Ctrl + Alt + 1).
/// Backspace or Delete clears it, Esc cancels, Tab moves on. Text is always in the format
/// Hotkey.TryParse understands ("Ctrl+Alt+1").
/// </summary>
public sealed partial class ShortcutBox : TextBox
{
    private string _beforeEdit = "";

    /// <summary>A full shortcut was recorded or cleared. Argument: new text ("" = none).</summary>
    public event Action<ShortcutBox, string>? Recorded;

    public ShortcutBox()
    {
        IsReadOnly = true;          // no caret typing, no IME: keys are captured instead
        IsSpellCheckEnabled = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        GotFocus += (_, _) =>
        {
            _beforeEdit = Text;
            PlaceholderText = S.PressShortcut;
        };
        LostFocus += (_, _) =>
        {
            // Left with only modifiers held ("Ctrl+Alt+…"): put the old value back.
            if (Text.EndsWith("…")) Text = _beforeEdit;
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;

        if (key == VirtualKey.Tab) return; // let focus move to the next field

        e.Handled = true;

        if (key == VirtualKey.Escape)
        {
            Text = _beforeEdit;
            return;
        }
        if (key is VirtualKey.Back or VirtualKey.Delete)
        {
            Text = "";
            Recorded?.Invoke(this, "");
            return;
        }

        var mods = new List<string>();
        if (IsDown(VirtualKey.Control)) mods.Add("Ctrl");
        if (IsDown(VirtualKey.Menu)) mods.Add("Alt");
        if (IsDown(VirtualKey.Shift)) mods.Add("Shift");
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods.Add("Win");

        if (IsModifier(key))
        {
            // Show what's held so far while the user builds the combination.
            Text = mods.Count > 0 ? string.Join("+", mods) + "+…" : "";
            return;
        }

        var name = KeyName(key);
        if (name == null) return;           // a key global shortcuts can't use (arrows, Enter…)
        if (mods.Count == 0)
        {
            Text = _beforeEdit;
            PlaceholderText = S.ShortcutNeedsModifier;
            return;
        }

        Text = string.Join("+", mods) + "+" + name;
        _beforeEdit = Text;
        Recorded?.Invoke(this, Text);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsModifier(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>Names match what Hotkey.TryParse accepts.</summary>
    private static string? KeyName(VirtualKey key)
    {
        int code = (int)key;
        if (code is >= 0x30 and <= 0x39) return ((char)code).ToString();             // 0-9
        if (code is >= 0x41 and <= 0x5A) return ((char)code).ToString();             // A-Z
        if (code is >= 0x60 and <= 0x69) return "Num" + (code - 0x60);               // numpad 0-9
        if (code is >= 0x70 and <= 0x87) return "F" + (code - 0x70 + 1);             // F1-F24
        return null;
    }
}
