using Volumy.Core.Audio;
using Volumy.Core.Icons;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Volumy.ViewModels;

public sealed class ProfileChip : ObservableObject
{
    private static readonly SolidColorBrush ActiveBackground = new(ColorHelper.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActiveBorder = new(ColorHelper.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HoverBackground = new(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HoverBorder = new(ColorHelper.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActiveHoverBackground = new(ColorHelper.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush Clear = new(Colors.Transparent);

    private bool _isActive;
    private bool _isHovered;

    public ProfileChip(string id, string name, string initial, string? color, string? hotkey, bool isActive)
    {
        Id = id;
        Hotkey = hotkey ?? "";
        Name = name;
        Initial = initial;
        Color = color;
        CircleBrush = ProfileColors.Brush(color);
        _isActive = isActive;
    }

    public string Id { get; }
    public string Name { get; }
    public string Initial { get; }
    public string? Color { get; }
    public string Hotkey { get; }
    public SolidColorBrush CircleBrush { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!Set(ref _isActive, value)) return;
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(BorderBrush));
            OnPropertyChanged(nameof(NameVisibility));
            OnPropertyChanged(nameof(ChipPadding));
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (!Set(ref _isHovered, value)) return;
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public SolidColorBrush Background => _isActive
        ? (_isHovered ? ActiveHoverBackground : ActiveBackground)
        : (_isHovered ? HoverBackground : Clear);

    public SolidColorBrush BorderBrush => _isActive ? ActiveBorder : (_isHovered ? HoverBorder : Clear);

    /// <summary>Active chip: circle + full name. Others: just the colored initial, so names never get cut.</summary>
    public Microsoft.UI.Xaml.Visibility NameVisibility =>
        _isActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Thickness ChipPadding => _isActive ? new(5, 0, 14, 0) : new(6, 0, 6, 0);
}

public sealed class DeviceItem
{
    public DeviceItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
}

public sealed class AppItem : ObservableObject
{
    private static readonly SolidColorBrush NeutralAccent = new(ColorHelper.FromArgb(0xFF, 0xD8, 0xD8, 0xE0));

    private readonly AudioService _audio;
    private readonly Action<AppItem>? _changedByUser;
    private bool _syncing;
    private double _volume;
    private bool _muted;

    public AppItem(AudioService audio, AppAudio app, Action<AppItem>? changedByUser = null)
    {
        _audio = audio;
        _changedByUser = changedByUser;
        Exe = app.Exe;
        Name = app.DisplayName;
        Letters = Name.Length >= 2 ? Name[..2] : Name;

        Accent = NeutralAccent;
        var icon = AppIconExtractor.Get(app.ExePath);
        if (icon != null)
        {
            Accent = new SolidColorBrush(ColorHelper.FromArgb(0xFF, icon.R, icon.G, icon.B));
            try
            {
                var bitmap = new BitmapImage();
                var stream = new MemoryStream(File.ReadAllBytes(icon.PngPath));
                bitmap.SetSource(stream.AsRandomAccessStream());
                Icon = bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // No icon: the row shows the app's initials instead.
                Volumy.Services.CrashLog.Trace($"icon for {app.Exe}: {ex.Message}");
            }
        }

        Sync(app.Volume, app.Muted);
    }

    public string Exe { get; }
    public string Name { get; }
    public string Letters { get; }
    public ImageSource? Icon { get; }
    public SolidColorBrush Accent { get; }

    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            OnPropertyChanged(nameof(VolumeText));
            if (_syncing) return;
            _audio.SetAppVolume(Exe, (float)(value / 100.0));
            _changedByUser?.Invoke(this);
        }
    }

    public string VolumeText => ((int)Math.Round(_volume)).ToString();

    public bool Muted
    {
        get => _muted;
        set
        {
            if (!Set(ref _muted, value)) return;
            OnPropertyChanged(nameof(MuteGlyph));
            if (_syncing) return;
            _audio.SetAppMute(Exe, value);
            _changedByUser?.Invoke(this);
        }
    }

    public string MuteGlyph => _muted ? Glyphs.Muted : Glyphs.Speaker;

    private double _peak;

    /// <summary>Current output level 0..100 (smoothed), for the live meter.</summary>
    public double Peak
    {
        get => _peak;
        set
        {
            if (Set(ref _peak, value)) OnPropertyChanged(nameof(PeakScale));
        }
    }

    /// <summary>0..1 horizontal scale of the level bar (anchored at the left edge).</summary>
    public double PeakScale => _peak / 100.0;

    /// <summary>Updates from the system without writing back to it.</summary>
    public void Sync(float volume, bool muted)
    {
        _syncing = true;
        Volume = Math.Round(volume * 100.0);
        Muted = muted;
        _syncing = false;
    }
}

public static class Glyphs
{
    public const string Speaker = "";
    public const string Muted = "";
    public const string Mic = "";
    public const string MicOff = "";
}
