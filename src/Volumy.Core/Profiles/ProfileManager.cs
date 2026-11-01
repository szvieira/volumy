using Volumy.Core.Audio;

namespace Volumy.Core.Profiles;

/// <summary>
/// Applies, captures and switches profiles. Not thread-safe: call it from the UI thread.
/// </summary>
public sealed class ProfileManager
{
    private readonly AudioService _audio;
    private readonly ProfileStore _store;

    public ProfileFile Data { get; private set; }

    /// <summary>Profiles were added, renamed, re-captured or reloaded from disk.</summary>
    public event Action? ProfilesChanged;

    /// <summary>A profile was applied. Argument: profile id.</summary>
    public event Action<string>? ProfileApplied;

    public string StorePath => _store.FilePath;

    /// <summary>True when the last write to profiles.json failed (file locked or unreadable).</summary>
    public bool SaveFailed => _store.LastSaveFailed;

    public string? ActiveId => Data.ActiveProfile;

    public Profile? Active =>
        ActiveId != null && Data.Profiles.TryGetValue(ActiveId, out var profile) ? profile : null;

    private readonly Func<int, string> _nameFor;

    public ProfileManager(AudioService audio, ProfileStore store, Func<int, string>? nameFor = null)
    {
        _audio = audio;
        _store = store;
        _nameFor = nameFor ?? (n => $"Profile {n}");
        Data = store.Load();
        EnsureValid();
    }

    public void Reload()
    {
        Data = _store.Load();
        EnsureValid();
        ProfilesChanged?.Invoke();
    }

    public void Apply(string profileId)
    {
        if (!Data.Profiles.TryGetValue(profileId, out var profile)) return;

        var outputs = _audio.GetDevices(DeviceFlow.Playback);
        var inputs = _audio.GetDevices(DeviceFlow.Capture);

        var playback = DeviceResolver.Resolve(profile.Playback, outputs);
        if (playback != null) _audio.SetDefault(playback, DeviceRole.Console, DeviceRole.Multimedia);

        var communications = DeviceResolver.Resolve(profile.Communications, outputs) ?? playback;
        if (communications != null) _audio.SetDefault(communications, DeviceRole.Communications);

        var microphone = DeviceResolver.Resolve(profile.Microphone, inputs);
        if (microphone != null)
        {
            _audio.SetDefault(microphone, DeviceRole.Console, DeviceRole.Multimedia, DeviceRole.Communications);
        }

        var all = outputs.Concat(inputs).ToList();
        foreach (var setting in profile.Devices)
        {
            var id = DeviceResolver.Resolve(setting, all);
            if (id == null) continue;
            _audio.SetDeviceVolume(id, setting.Volume);
            _audio.SetDeviceMute(id, setting.Muted);
        }

        if (profile.Apps.Count > 0)
        {
            _audio.ApplyAppRules(exe =>
                profile.Apps.TryGetValue(exe, out var rule) ? (rule.Volume, rule.Muted) : null);
        }

        Data.ActiveProfile = profileId;
        _store.Save(Data);
        ProfileApplied?.Invoke(profileId);
    }

    /// <summary>Call when an app starts playing, so it gets the active profile's volume.</summary>
    public void OnAppSessionStarted(string exe)
    {
        var profile = Active;
        if (profile != null) ApplyAppRule(profile, exe);
    }

    /// <summary>Call when a device is plugged in; switches to the profile bound to it, if any.</summary>
    public void OnDeviceActivated(string deviceId)
    {
        var device = _audio.GetDevices(DeviceFlow.Playback)
            .Concat(_audio.GetDevices(DeviceFlow.Capture))
            .FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return;

        foreach (var (id, profile) in Data.Profiles)
        {
            if (profile.AutoSwitchDevice != null && DeviceResolver.Matches(profile.AutoSwitchDevice, device))
            {
                if (id != ActiveId) Apply(id);
                return;
            }
        }
    }

    /// <summary>Overwrites a profile's devices and volumes with what is set right now.</summary>
    public void SaveCurrentInto(string profileId)
    {
        if (!Data.Profiles.TryGetValue(profileId, out var profile)) return;
        Capture(profile);
        _store.Save(Data);
        ProfilesChanged?.Invoke();
    }

    /// <summary>Creates "Profile N" from the current state and returns its id.</summary>
    public string CreateFromCurrent()
    {
        int n = Data.Profiles.Count + 1;
        while (Data.Profiles.ContainsKey($"perfil-{n}")) n++;
        var id = $"perfil-{n}";
        var profile = new Profile
        {
            Name = _nameFor(n),
            Hotkey = n <= 9 ? $"Ctrl+Alt+{n}" : null,
        };
        Data.Profiles[id] = Capture(profile);
        _store.Save(Data);
        ProfilesChanged?.Invoke();
        return id;
    }

    /// <summary>Deep copy for editing, so unsaved changes never touch the live profile.</summary>
    public Profile CloneProfile(string profileId) => ProfileStore.Clone(Data.Profiles[profileId]);

    /// <summary>Stores an edited profile. Re-applies it if it is the active one.</summary>
    public void ReplaceProfile(string profileId, Profile profile)
    {
        Data.Profiles[profileId] = profile;
        _store.Save(Data);
        ProfilesChanged?.Invoke();
        if (profileId == ActiveId) Apply(profileId);
    }

    /// <summary>Deletes a profile. The last profile can't be deleted.</summary>
    public bool DeleteProfile(string profileId)
    {
        if (Data.Profiles.Count <= 1 || !Data.Profiles.Remove(profileId)) return false;
        if (ActiveId == profileId)
        {
            Data.ActiveProfile = Data.Profiles.Keys.First();
            _store.Save(Data);
            Apply(Data.ActiveProfile);
        }
        else
        {
            _store.Save(Data);
        }
        ProfilesChanged?.Invoke();
        return true;
    }

    public void SetRestoreOnStartup(bool value)
    {
        if (Data.RestoreOnStartup == value) return;
        Data.RestoreOnStartup = value;
        _store.Save(Data);
    }

    public void SetMicMuteHotkey(string? hotkey)
    {
        if (Data.MicMuteHotkey == hotkey) return;
        Data.MicMuteHotkey = hotkey;
        _store.Save(Data);
        ProfilesChanged?.Invoke();
    }

    public void SetLanguage(string code)
    {
        if (Data.Language == code) return;
        Data.Language = code;
        _store.Save(Data);
    }

    public void SetPlaySound(bool value)
    {
        if (Data.PlaySound == value) return;
        Data.PlaySound = value;
        _store.Save(Data);
    }

    private Profile Capture(Profile profile)
    {
        var outputs = _audio.GetDevices(DeviceFlow.Playback);
        var inputs = _audio.GetDevices(DeviceFlow.Capture);

        var playback = _audio.GetDefaultId(DeviceFlow.Playback, DeviceRole.Multimedia);
        var communications = _audio.GetDefaultId(DeviceFlow.Playback, DeviceRole.Communications);
        var microphone = _audio.GetDefaultId(DeviceFlow.Capture, DeviceRole.Multimedia);

        profile.Playback = DeviceResolver.ToRef(playback, outputs);
        profile.Communications = DeviceResolver.ToRef(communications, outputs);
        profile.Microphone = DeviceResolver.ToRef(microphone, inputs);

        var all = outputs.Concat(inputs).ToList();
        profile.Devices = new List<DeviceSetting>();
        foreach (var id in new[] { playback, communications, microphone }.Where(i => i != null).Distinct())
        {
            // The default can change between the two calls (device unplugged): skip it instead of throwing.
            var device = all.FirstOrDefault(d => d.Id == id);
            if (device == null) continue;
            profile.Devices.Add(new DeviceSetting
            {
                Id = device.Id,
                Name = device.Name,
                Volume = _audio.GetDeviceVolume(device.Id) ?? 1f,
                Muted = _audio.GetDeviceMute(device.Id) ?? false,
            });
        }

        foreach (var app in _audio.GetApps())
        {
            if (!profile.Apps.TryGetValue(app.Exe, out var rule))
            {
                rule = new AppRule();
                profile.Apps[app.Exe] = rule;
            }
            rule.Volume = (float)Math.Round(app.Volume, 2);
            rule.Muted = app.Muted;
        }

        return profile;
    }

    private void ApplyAppRule(Profile profile, string exe)
    {
        if (!profile.Apps.TryGetValue(exe, out var rule)) return;
        if (rule.Volume is float volume) _audio.SetAppVolume(exe, volume);
        if (rule.Muted is bool muted) _audio.SetAppMute(exe, muted);
    }

    private void EnsureValid()
    {
        if (Data.Profiles.Count == 0)
        {
            var profile = new Profile { Name = _nameFor(1), Hotkey = "Ctrl+Alt+1" };
            Data.Profiles["perfil-1"] = Capture(profile);
            Data.ActiveProfile = "perfil-1";
            _store.Save(Data);
        }
        else if (ActiveId == null || !Data.Profiles.ContainsKey(ActiveId))
        {
            Data.ActiveProfile = Data.Profiles.Keys.First();
        }
    }
}
