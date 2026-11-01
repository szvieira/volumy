namespace Volumy.ViewModels;

public sealed class ProfileListItem
{
    public ProfileListItem(string id, string name, string initial, string? color, string subtitle)
    {
        Id = id;
        Name = name;
        Initial = initial;
        Subtitle = subtitle;
        CircleBrush = ProfileColors.Brush(color);
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush CircleBrush { get; }
    public string Id { get; }
    public string Name { get; }
    public string Initial { get; }
    public string Subtitle { get; }
}

/// <summary>An entry in a device picker. Id "" means "none".</summary>
public sealed class DeviceOption
{
    public DeviceOption(string id, string name, string? realName = null)
    {
        Id = id;
        Name = name;
        RealName = realName ?? name;
    }

    public string Id { get; }

    /// <summary>Text shown in the picker (may carry a "disconnected" suffix).</summary>
    public string Name { get; }

    /// <summary>The device's own name, saved to the profile.</summary>
    public string RealName { get; }
}

/// <summary>A switch sound: Value is "builtin:&lt;id&gt;" or a file path.</summary>
public sealed class SoundOption
{
    public SoundOption(string value, string name)
    {
        Value = value;
        Name = name;
    }

    public string Value { get; }
    public string Name { get; }
}

public sealed class LanguageOption
{
    public LanguageOption(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }
}

public sealed class RuleItem : ObservableObject
{
    private double _volume;
    private bool _muted;

    public RuleItem(string exe, double volume, bool muted)
    {
        Exe = exe;
        _volume = volume;
        _muted = muted;
    }

    public string Exe { get; }

    public double Volume
    {
        get => _volume;
        set
        {
            if (Set(ref _volume, value)) OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => $"{(int)Math.Round(_volume)}%";

    public bool Muted
    {
        get => _muted;
        set => Set(ref _muted, value);
    }
}
