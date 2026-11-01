using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Volumy.ViewModels;

/// <summary>
/// Profile colors. All presets are light enough for the dark initial letter on top of them.
/// </summary>
public static class ProfileColors
{
    public static readonly (string? Hex, string Name)[] Presets =
    {
        (null, "Branco"),
        ("#7FB2FF", "Azul"),
        ("#5EE3CD", "Verde-água"),
        ("#8BD17C", "Verde"),
        ("#FFD166", "Amarelo"),
        ("#FFB86B", "Laranja"),
        ("#FF8A8A", "Vermelho"),
        ("#FF9CC8", "Rosa"),
        ("#C3A2FF", "Lilás"),
    };

    private static readonly Color Neutral = ColorHelper.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);

    public static Color Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Neutral;
        var text = hex.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return Neutral;
    }

    public static SolidColorBrush Brush(string? hex) => new(Parse(hex));
}
