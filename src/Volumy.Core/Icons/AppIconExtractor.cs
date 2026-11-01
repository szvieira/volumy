using System.Drawing;
using System.Drawing.Imaging;

namespace Volumy.Core.Icons;

public sealed record AppIcon(string PngPath, byte R, byte G, byte B);

/// <summary>
/// Extracts an exe's icon to a cached PNG and picks its dominant vivid color.
/// That color tints the app's slider, so the UI only gets color from app icons.
/// </summary>
public static class AppIconExtractor
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Volumy",
        "icons");

    private static readonly Dictionary<string, AppIcon?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static AppIcon? Get(string? exePath)
    {
        if (exePath == null) return null;
        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        AppIcon? result = null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon != null)
            {
                using var bitmap = icon.ToBitmap();
                Directory.CreateDirectory(CacheDir);
                var png = Path.Combine(CacheDir, Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant() + ".png");
                bitmap.Save(png, ImageFormat.Png);
                var (r, g, b) = DominantColor(bitmap);
                result = new AppIcon(png, r, g, b);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            result = null;
        }

        Cache[exePath] = result;
        return result;
    }

    /// <summary>Saturation-weighted average, lifted so it reads on dark glass.</summary>
    private static (byte R, byte G, byte B) DominantColor(Bitmap bitmap)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.A < 128) continue;
                double saturation = c.GetSaturation();
                double brightness = c.GetBrightness();
                double weight = saturation * saturation * (brightness is > 0.15 and < 0.95 ? 1 : 0.2);
                r += c.R * weight;
                g += c.G * weight;
                b += c.B * weight;
                total += weight;
            }
        }

        if (total < 1.0)
        {
            return (0xD8, 0xD8, 0xE0); // Grey/monochrome icon: neutral light grey.
        }

        var color = Color.FromArgb((int)(r / total), (int)(g / total), (int)(b / total));
        // Lift dark colors so the slider fill stays visible on dark glass.
        double lift = Math.Max(0, 0.55 - color.GetBrightness());
        return (Lift(color.R, lift), Lift(color.G, lift), Lift(color.B, lift));
    }

    private static byte Lift(byte channel, double amount) =>
        (byte)Math.Min(255, channel + (255 - channel) * amount);
}
