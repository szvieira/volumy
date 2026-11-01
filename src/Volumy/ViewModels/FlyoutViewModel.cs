using Volumy.Localization;
using System.Collections.ObjectModel;
using Volumy.Core.Audio;
using Volumy.Core.Profiles;
using Microsoft.UI.Xaml;

namespace Volumy.ViewModels;

public sealed class FlyoutViewModel : ObservableObject
{
    private readonly AudioService _audio;
    private readonly ProfileManager _profiles;
    private bool _refreshing;

    private int _outputIndex = -1;
    private int _inputIndex = -1;
    private double _outputVolume;
    private double _inputVolume;
    private bool _outputMuted;
    private bool _inputMuted;
    private string _hotkeyHint = "";
    private string _saveLabel = "";

    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;

    public FlyoutViewModel(AudioService audio, ProfileManager profiles)
    {
        _audio = audio;
        _profiles = profiles;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<ProfileChip> Profiles { get; } = new();
    public ObservableCollection<DeviceItem> Outputs { get; } = new();
    public ObservableCollection<DeviceItem> Inputs { get; } = new();
    public ObservableCollection<AppItem> Apps { get; } = new();

    public int OutputIndex
    {
        get => _outputIndex;
        private set { _outputIndex = value; OnPropertyChanged(); }
    }

    public int InputIndex
    {
        get => _inputIndex;
        private set { _inputIndex = value; OnPropertyChanged(); }
    }

    public double OutputVolume
    {
        get => _outputVolume;
        set
        {
            if (!Set(ref _outputVolume, value)) return;
            OnPropertyChanged(nameof(OutputVolumeText));
            if (!_refreshing && CurrentOutputId is string id) _audio.SetDeviceVolume(id, (float)(value / 100.0));
        }
    }

    public string OutputVolumeText => ((int)Math.Round(_outputVolume)).ToString();

    public bool OutputMuted
    {
        get => _outputMuted;
        set
        {
            if (!Set(ref _outputMuted, value)) return;
            OnPropertyChanged(nameof(OutputGlyph));
            if (!_refreshing && CurrentOutputId is string id) _audio.SetDeviceMute(id, value);
        }
    }

    public string OutputGlyph => _outputMuted ? Glyphs.Muted : Glyphs.Speaker;

    public double InputVolume
    {
        get => _inputVolume;
        set
        {
            if (!Set(ref _inputVolume, value)) return;
            OnPropertyChanged(nameof(InputVolumeText));
            if (!_refreshing && CurrentInputId is string id) _audio.SetDeviceVolume(id, (float)(value / 100.0));
        }
    }

    public string InputVolumeText => ((int)Math.Round(_inputVolume)).ToString();

    public bool InputMuted
    {
        get => _inputMuted;
        set
        {
            if (!Set(ref _inputMuted, value)) return;
            OnPropertyChanged(nameof(InputGlyph));
            if (!_refreshing && CurrentInputId is string id)
            {
                _audio.SetDeviceMute(id, value);
                InputMuteChanged?.Invoke();
            }
        }
    }

    /// <summary>The user muted/unmuted the mic from the flyout (the tray badge listens).</summary>
    public event Action? InputMuteChanged;

    // ---------- Level meters ----------

    private Func<float>? _outputMeter;
    private Func<float>? _inputMeter;
    private Dictionary<string, List<Func<float>>> _appMeters = new(StringComparer.OrdinalIgnoreCase);
    private double _outputPeak;
    private double _inputPeak;

    /// <summary>0..100, smoothed. Drives the thin bar under the output slider.</summary>
    public double OutputPeak
    {
        get => _outputPeak;
        private set
        {
            if (Set(ref _outputPeak, value)) OnPropertyChanged(nameof(OutputPeakScale));
        }
    }

    /// <summary>0..1 horizontal scale of the level bar (anchored at the left edge).</summary>
    public double OutputPeakScale => _outputPeak / 100.0;

    /// <summary>0..100, smoothed. Shows you are being heard.</summary>
    public double InputPeak
    {
        get => _inputPeak;
        private set
        {
            if (Set(ref _inputPeak, value)) OnPropertyChanged(nameof(InputPeakScale));
        }
    }

    public double InputPeakScale => _inputPeak / 100.0;

    /// <summary>Called ~25 times a second while the flyout is open.</summary>
    public void UpdateMeters()
    {
        OutputPeak = Smooth(_outputPeak, _outputMeter?.Invoke() ?? 0f);
        InputPeak = Smooth(_inputPeak, _inputMeter?.Invoke() ?? 0f);
        foreach (var app in Apps)
        {
            float peak = 0f;
            if (_appMeters.TryGetValue(app.Exe, out var meters))
            {
                foreach (var meter in meters) peak = Math.Max(peak, meter());
            }
            app.Peak = Smooth(app.Peak, peak);
        }
    }

    /// <summary>Jump up instantly, fall back gently, like a real VU meter.</summary>
    private static double Smooth(double previous, float raw)
    {
        double target = raw * 100.0;
        double next = target >= previous ? target : previous * 0.82;
        return next < 0.5 ? 0 : Math.Round(next, 1);
    }

    private void RefreshDeviceMeters()
    {
        _audio.ReleaseDeviceMeters();
        _outputMeter = CurrentOutputId is string output ? _audio.GetDeviceMeter(output) : null;
        _inputMeter = CurrentInputId is string input ? _audio.GetDeviceMeter(input) : null;
    }

    public string InputGlyph => _inputMuted ? Glyphs.MicOff : Glyphs.Mic;

    public string HotkeyHint
    {
        get => _hotkeyHint;
        private set => Set(ref _hotkeyHint, value);
    }

    public string SaveLabel
    {
        get => _saveLabel;
        private set => Set(ref _saveLabel, value);
    }

    // Active chip (with name) + 3 initials fit in the 380 px flyout; the rest go in "+N".
    private const int MaxChips = 4;

    /// <summary>The chips shown in the capsule (at most 4; always includes the active profile).</summary>
    public ObservableCollection<ProfileChip> VisibleChips { get; } = new();

    /// <summary>Profiles that don't fit as chips; listed in the "+N" dropdown.</summary>
    public IReadOnlyList<ProfileChip> OverflowChips { get; private set; } = Array.Empty<ProfileChip>();

    public Visibility OverflowVisibility => OverflowChips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string OverflowText => $"+{OverflowChips.Count}";

    /// <summary>The "+" button only fits when there's no "+N" dropdown.</summary>
    public Visibility NewButtonVisibility => OverflowChips.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    private void SplitChips()
    {
        var all = Profiles.ToList();
        var visible = all.Take(MaxChips).ToList();
        var active = all.FirstOrDefault(c => c.Id == _profiles.ActiveId);
        if (active != null && !visible.Contains(active))
        {
            // Keep the active profile on screen: it replaces the last chip.
            visible = all.Take(MaxChips - 1).Append(active).ToList();
        }

        if (!visible.SequenceEqual(VisibleChips))
        {
            VisibleChips.Clear();
            foreach (var chip in visible) VisibleChips.Add(chip);
        }
        OverflowChips = all.Except(visible).ToList();
        OnPropertyChanged(nameof(OverflowVisibility));
        OnPropertyChanged(nameof(OverflowText));
        OnPropertyChanged(nameof(NewButtonVisibility));
    }

    public Visibility NoAppsVisibility => Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string AppCountText => S.AppCount(Apps.Count);

    private string? CurrentOutputId => _outputIndex >= 0 && _outputIndex < Outputs.Count ? Outputs[_outputIndex].Id : null;
    private string? CurrentInputId => _inputIndex >= 0 && _inputIndex < Inputs.Count ? Inputs[_inputIndex].Id : null;

    // ---------- Actions ----------

    /// <summary>
    /// Highlights the chip right away, then applies the profile on the next UI tick,
    /// so the click feels instant even though switching devices takes a moment.
    /// </summary>
    public void SelectProfile(string id)
    {
        if (id == _profiles.ActiveId) return;
        foreach (var chip in Profiles) chip.IsActive = chip.Id == id;
        _ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => Volumy.Services.Guard.Run("switch profile", () => _profiles.Apply(id)));
    }

    public void SaveIntoActive()
    {
        if (_profiles.ActiveId is not string id) return;
        _profiles.SaveCurrentInto(id);
        HotkeyHint = S.StateSavedTo(_profiles.Active?.Name ?? "");
    }

    public string NewProfileFromCurrent() => _profiles.CreateFromCurrent();

    public void SelectOutput(int index)
    {
        if (_refreshing || index < 0 || index >= Outputs.Count || index == _outputIndex) return;
        _audio.SetDefault(Outputs[index].Id, DeviceRole.Console, DeviceRole.Multimedia);
        _outputIndex = index;
        LoadDeviceLevels();
        RefreshDeviceMeters();
    }

    public void SelectInput(int index)
    {
        if (_refreshing || index < 0 || index >= Inputs.Count || index == _inputIndex) return;
        _audio.SetDefault(Inputs[index].Id, DeviceRole.Console, DeviceRole.Multimedia, DeviceRole.Communications);
        _inputIndex = index;
        LoadDeviceLevels();
        RefreshDeviceMeters();
    }

    // ---------- Refresh from the system ----------

    public void Refresh()
    {
        RefreshProfiles();
        RefreshDevices();
        RefreshApps();
    }

    public void RefreshProfiles()
    {
        // Update chips in place when only the active one changed, so the highlight can animate.
        var data = _profiles.Data.Profiles;
        bool same = data.Count == Profiles.Count && Profiles.All(c =>
            data.TryGetValue(c.Id, out var p) && p.Name == c.Name && p.Color == c.Color && (p.Hotkey ?? "") == c.Hotkey);
        if (same)
        {
            foreach (var chip in Profiles) chip.IsActive = chip.Id == _profiles.ActiveId;
        }
        else
        {
            Profiles.Clear();
            foreach (var (id, profile) in data)
            {
                Profiles.Add(new ProfileChip(id, profile.Name, profile.Initial, profile.Color, profile.Hotkey, id == _profiles.ActiveId));
            }
        }

        SplitChips();

        var active = _profiles.Active;
        HotkeyHint = active?.Hotkey is string hotkey ? S.HotkeyActivates(hotkey) : "";
        SaveLabel = active != null ? S.SaveStateTo(active.Name) : S.SaveState;
    }

    public void RefreshDevices()
    {
        _refreshing = true;
        try
        {
            Fill(Outputs, _audio.GetDevices(DeviceFlow.Playback));
            Fill(Inputs, _audio.GetDevices(DeviceFlow.Capture));
            OutputIndex = IndexOf(Outputs, _audio.GetDefaultId(DeviceFlow.Playback, DeviceRole.Multimedia));
            InputIndex = IndexOf(Inputs, _audio.GetDefaultId(DeviceFlow.Capture, DeviceRole.Multimedia));
        }
        finally
        {
            _refreshing = false;
        }
        LoadDeviceLevels();
        RefreshDeviceMeters();
    }

    public void RefreshApps()
    {
        _appMeters = _audio.GetSessionMeters();
        var current = _audio.GetApps();
        bool sameSet = current.Count == Apps.Count &&
                       current.Select(a => a.Exe).SequenceEqual(Apps.Select(a => a.Exe));
        if (sameSet)
        {
            for (int i = 0; i < current.Count; i++)
            {
                Apps[i].Sync(current[i].Volume, current[i].Muted);
            }
        }
        else
        {
            Apps.Clear();
            foreach (var app in current)
            {
                Apps.Add(new AppItem(_audio, app));
            }
        }
        OnPropertyChanged(nameof(NoAppsVisibility));
        OnPropertyChanged(nameof(AppCountText));
    }

    private void LoadDeviceLevels()
    {
        _refreshing = true;
        try
        {
            if (CurrentOutputId is string output)
            {
                OutputVolume = Math.Round((_audio.GetDeviceVolume(output) ?? 0f) * 100.0);
                OutputMuted = _audio.GetDeviceMute(output) ?? false;
            }
            if (CurrentInputId is string input)
            {
                InputVolume = Math.Round((_audio.GetDeviceVolume(input) ?? 0f) * 100.0);
                InputMuted = _audio.GetDeviceMute(input) ?? false;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void Fill(ObservableCollection<DeviceItem> target, IReadOnlyList<AudioDevice> devices)
    {
        target.Clear();
        foreach (var device in devices)
        {
            target.Add(new DeviceItem(device.Id, device.Name));
        }
    }

    private static int IndexOf(ObservableCollection<DeviceItem> items, string? id)
    {
        if (id == null) return -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id) return i;
        }
        return -1;
    }
}
