namespace Volumy.Core.Profiles;

/// <summary>
/// A device is stored by id (stable) plus friendly name (fallback when a driver
/// reinstall changes the id).
/// </summary>
public sealed class DeviceRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class DeviceSetting
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float Volume { get; set; }
    public bool Muted { get; set; }
}

/// <summary>Rule applied to every audio session of one exe.</summary>
public sealed class AppRule
{
    public float? Volume { get; set; }
    public bool? Muted { get; set; }

    /// <summary>Reserved for per-app output routing (not applied yet).</summary>
    public DeviceRef? Output { get; set; }
}

public sealed class Profile
{
    /// <summary>Longest allowed profile name (chips in the flyout are narrow).</summary>
    public const int MaxNameLength = 20;

    public string Name { get; set; } = "";

    /// <summary>Hex color like "#7FB2FF" for the profile's circle. Null = neutral white.</summary>
    public string? Color { get; set; }

    /// <summary>Switch sound: "builtin:&lt;id&gt;" or a file path. Null = default sound.</summary>
    public string? Sound { get; set; }

    /// <summary>Global hotkey, e.g. "Ctrl+Alt+1".</summary>
    public string? Hotkey { get; set; }

    /// <summary>When this device is plugged in, this profile is applied.</summary>
    public DeviceRef? AutoSwitchDevice { get; set; }

    public DeviceRef? Playback { get; set; }
    public DeviceRef? Communications { get; set; }
    public DeviceRef? Microphone { get; set; }

    public List<DeviceSetting> Devices { get; set; } = new();

    /// <summary>Keyed by lower-case exe name, e.g. "spotify.exe".</summary>
    public Dictionary<string, AppRule> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [System.Text.Json.Serialization.JsonIgnore]
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
}

public sealed class ProfileFile
{
    public string? ActiveProfile { get; set; }
    public bool RestoreOnStartup { get; set; } = true;

    /// <summary>Play a chime when the profile changes.</summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>UI language: "en" (default) or "pt-BR".</summary>
    public string Language { get; set; } = "en";

    /// <summary>Global shortcut that toggles the microphone. Null = none.</summary>
    public string? MicMuteHotkey { get; set; } = "Ctrl+Alt+M";
    public Dictionary<string, Profile> Profiles { get; set; } = new();
}
