namespace Volumy.Core.Hotkeys;

/// <summary>Parses "Ctrl+Alt+1" into RegisterHotKey modifiers and a virtual-key code.</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint modifiers = 0;
        uint? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "WIN":
                    modifiers |= ModWin;
                    break;
                default:
                    if (key != null) return false;
                    key = ParseKey(raw.ToUpperInvariant());
                    if (key == null) return false;
                    break;
            }
        }

        if (key == null || modifiers == 0) return false;
        hotkey = new Hotkey(modifiers, key.Value);
        return true;
    }

    private static uint? ParseKey(string key)
    {
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= '0' and <= '9') return (uint)c;          // VK_0..VK_9
            if (c is >= 'A' and <= 'Z') return (uint)c;          // VK_A..VK_Z
            return null;
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
        {
            return (uint)(0x70 + f - 1);                          // VK_F1..VK_F24
        }
        if (key.StartsWith("NUM") && int.TryParse(key[3..], out int n) && n is >= 0 and <= 9)
        {
            return (uint)(0x60 + n);                              // VK_NUMPAD0..9
        }
        return null;
    }
}
