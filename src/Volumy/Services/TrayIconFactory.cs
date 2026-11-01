using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Volumy.Interop;

namespace Volumy.Services;

/// <summary>Draws the tray icon: the active profile's initial on its color, plus a red badge when the mic is muted.</summary>
internal sealed class TrayIconFactory : IDisposable
{
    public Icon Create(string initial, string? hexColor = null, bool micMuted = false)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            var c = ViewModels.ProfileColors.Parse(hexColor);
            using var fill = new SolidBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            g.FillEllipse(fill, 1, 1, size - 2, size - 2);
            using var font = new Font("Segoe UI", 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.FromArgb(255, 17, 17, 20));
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(initial, font, text, new RectangleF(0, 1, size, size), format);

            if (micMuted)
            {
                // Red badge, bottom-right, with a dark ring so it reads on any taskbar color.
                using var ring = new SolidBrush(Color.FromArgb(255, 20, 20, 24));
                using var red = new SolidBrush(Color.FromArgb(255, 255, 92, 92));
                g.FillEllipse(ring, 17, 17, 15, 15);
                g.FillEllipse(red, 19, 19, 11, 11);
                using var slash = new Pen(Color.White, 2f);
                g.DrawLine(slash, 21.5f, 27.5f, 27.5f, 21.5f);
            }
        }

        // Hand the tray an icon that owns its own handle. The tray disposes the previous icon itself
        // when we assign a new one, so we must not destroy a handle it may still be drawing.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            Win32.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        // Nothing to release: the icons belong to the tray icon once assigned.
    }
}
