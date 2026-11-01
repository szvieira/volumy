using System.Collections.ObjectModel;
using Volumy.Core.Audio;
using Volumy.Core.Hotkeys;
using Volumy.Core.Profiles;
using Volumy.Interop;
using Volumy.Localization;
using Volumy.Services;
using Volumy.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Volumy;

/// <summary>Profile editor: name, hotkey, color, sound, auto-switch device, default devices and app rules.</summary>
public sealed partial class ProfilesWindow : Window
{
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedRing = new(Microsoft.UI.Colors.White);
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush HoverRing =
        new(Microsoft.UI.ColorHelper.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush IdleRing =
        new(Microsoft.UI.ColorHelper.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private readonly AudioService _audio;
    private readonly ProfileManager _profiles;
    private readonly IntPtr _hwnd;
    private readonly ObservableCollection<ProfileListItem> _items = new();
    private readonly ObservableCollection<RuleItem> _rules = new();
    private readonly List<Button> _swatches = new();

    private List<DeviceOption> _outputs = new();
    private List<DeviceOption> _inputs = new();
    private string? _currentId;
    private Profile? _draft;
    private bool _loading;
    private string? _selectedColor;
    private string _selectedSound = SoundPlayer.DefaultSound;

    public ProfilesWindow(AudioService audio, ProfileManager profiles)
    {
        _audio = audio;
        _profiles = profiles;
        InitializeComponent();
        Title = S.ProfilesTitle;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);

        _hwnd = WindowNative.GetWindowHandle(this);
        double scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1080 * scale), (int)(850 * scale)));

        ProfileList.ItemsSource = _items;
        RulesList.ItemsSource = _rules;
        _rules.CollectionChanged += (_, _) => UpdateNoRules();

        _loading = true;
        RestoreToggle.IsOn = _profiles.Data.RestoreOnStartup;
        SoundToggle.IsOn = _profiles.Data.PlaySound;
        StartupToggle.IsOn = Startup.IsEnabled;
        MicHotkeyBox.Text = _profiles.Data.MicMuteHotkey ?? "";
        MicHotkeyBox.Recorded += (box, text) => Guard.Run("mic shortcut", () => OnMicHotkeyRecorded(box, text));
        var languages = S.Languages.Select(l => new LanguageOption(l.Code, l.Name)).ToList();
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.First(l => l.Code == S.Language);
        _loading = false;

        BuildSwatches();
        LoadDevices();
        RefreshList(_profiles.ActiveId);
    }

    // ---------- Colors ----------

    private void BuildSwatches()
    {
        foreach (var (hex, _) in ProfileColors.Presets)
        {
            var name = S.ColorName(hex);
            var swatch = new HandButton
            {
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(17),
                Background = ProfileColors.Brush(hex),
                Tag = hex,
            };
            // Keep the color visible on hover/press (the default style paints it grey); show a ring instead.
            var fill = ProfileColors.Brush(hex);
            swatch.Resources["ButtonBackgroundPointerOver"] = fill;
            swatch.Resources["ButtonBackgroundPressed"] = fill;
            swatch.Resources["ButtonBorderBrushPointerOver"] = HoverRing;
            swatch.Resources["ButtonBorderBrushPressed"] = SelectedRing;
            ToolTipService.SetToolTip(swatch, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, name);
            swatch.Click += (_, _) => SelectColor(hex);
            _swatches.Add(swatch);
            ColorPanel.Children.Add(swatch);
        }
    }

    private void SelectColor(string? hex)
    {
        _selectedColor = hex;
        foreach (var swatch in _swatches)
        {
            bool selected = string.Equals(swatch.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = selected ? SelectedRing : IdleRing;
            swatch.BorderThickness = new Thickness(selected ? 3 : 2);
        }
    }

    // ---------- Sounds ----------

    private void FillSounds(string? current)
    {
        var options = SoundPlayer.BuiltIns
            .Select(id => new SoundOption("builtin:" + id, S.SoundName(id)))
            .ToList();

        var value = current ?? SoundPlayer.DefaultSound;
        var selected = options.FirstOrDefault(o => o.Value == value);
        if (selected == null)
        {
            // A user file: list it at the end, even if it went missing (it then falls back to the chime).
            selected = new SoundOption(value, S.CustomSound(Path.GetFileName(value)));
            options.Add(selected);
        }

        SoundBox.ItemsSource = options;
        SoundBox.SelectedItem = selected;
        _selectedSound = selected.Value;
    }

    private void SoundBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoundBox.SelectedItem is not SoundOption option) return;
        _selectedSound = option.Value;
        if (!_loading) SoundPlayer.Play(option.Value);
    }

    private void PreviewSound_Click(object sender, RoutedEventArgs e) => SoundPlayer.Play(_selectedSound);

    // async void: an exception here would bypass every handler and crash, so catch everything.
    private async void ChooseSound_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
            foreach (var type in SoundPlayer.FileTypes) picker.FileTypeFilter.Add(type);
            InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var imported = SoundPlayer.ImportCustom(file.Path);
            FillSounds(imported);
            SoundPlayer.Play(imported);
        }
        catch (Exception ex)
        {
            CrashLog.Write("choose sound", ex);
            StatusText.Text = S.SoundFileError;
        }
    }

    // ---------- Loading ----------

    private void LoadDevices()
    {
        _outputs = _audio.GetDevices(DeviceFlow.Playback).Select(d => new DeviceOption(d.Id, d.Name)).ToList();
        _inputs = _audio.GetDevices(DeviceFlow.Capture).Select(d => new DeviceOption(d.Id, d.Name)).ToList();
    }

    private void RefreshList(string? select)
    {
        _loading = true;
        _items.Clear();
        foreach (var (id, profile) in _profiles.Data.Profiles)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(profile.Hotkey)) parts.Add(profile.Hotkey);
            if (id == _profiles.ActiveId) parts.Add(S.ActiveTag);
            _items.Add(new ProfileListItem(id, profile.Name, profile.Initial, profile.Color, string.Join(" · ", parts)));
        }

        var target = _items.FirstOrDefault(i => i.Id == select) ?? _items.FirstOrDefault();
        ProfileList.SelectedItem = target;
        _loading = false;

        if (target != null) Load(target.Id);
        DeleteButton.IsEnabled = _items.Count > 1;
    }

    private void Load(string id)
    {
        _loading = true;
        _currentId = id;
        _draft = _profiles.CloneProfile(id);

        NameBox.Text = _draft.Name;
        HotkeyBox.Text = _draft.Hotkey ?? "";
        SelectColor(_draft.Color);
        FillSounds(_draft.Sound);

        var allDevices = _outputs.Concat(_inputs).ToList();
        Fill(AutoDeviceBox, allDevices, _draft.AutoSwitchDevice, S.None);
        Fill(PlaybackBox, _outputs, _draft.Playback, S.DontChange);
        Fill(CommunicationsBox, _outputs, _draft.Communications, S.SameAsOutput);
        Fill(MicrophoneBox, _inputs, _draft.Microphone, S.DontChange);

        _rules.Clear();
        foreach (var (exe, rule) in _draft.Apps.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
        {
            _rules.Add(new RuleItem(exe, Math.Round((rule.Volume ?? 1f) * 100), rule.Muted ?? false));
        }
        UpdateNoRules();
        StatusText.Text = "";
        _loading = false;
    }

    /// <summary>Fills a picker: "none" first, then devices; a saved device that is unplugged stays selectable.</summary>
    private static void Fill(ComboBox box, List<DeviceOption> devices, DeviceRef? saved, string noneLabel)
    {
        var options = new List<DeviceOption> { new("", noneLabel) };
        options.AddRange(devices);

        DeviceOption selected = options[0];
        if (saved != null)
        {
            selected = devices.FirstOrDefault(d => d.Id == saved.Id)
                       ?? devices.FirstOrDefault(d => string.Equals(d.Name, saved.Name, StringComparison.OrdinalIgnoreCase))
                       ?? AddMissing(options, saved);
        }

        box.ItemsSource = options;
        box.SelectedItem = selected;
    }

    private static DeviceOption AddMissing(List<DeviceOption> options, DeviceRef saved)
    {
        var missing = new DeviceOption(saved.Id, $"{saved.Name} ({S.Disconnected})", saved.Name);
        options.Add(missing);
        return missing;
    }

    private static DeviceRef? Read(ComboBox box) =>
        box.SelectedItem is DeviceOption { Id.Length: > 0 } option
            ? new DeviceRef { Id = option.Id, Name = option.RealName }
            : null;

    private void UpdateNoRules() =>
        NoRulesText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---------- Events ----------

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ProfileList.SelectedItem is ProfileListItem item && item.Id != _currentId) Load(item.Id);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_currentId == null || _draft == null) return;

        var name = NameBox.Text.Trim();
        if (name.Length > Profile.MaxNameLength) name = name[..Profile.MaxNameLength].TrimEnd();
        if (name.Length == 0)
        {
            StatusText.Text = S.NeedName;
            return;
        }

        var hotkeyText = HotkeyBox.Text.Trim();
        string? hotkey = null;
        if (hotkeyText.Length > 0)
        {
            if (!Hotkey.TryParse(hotkeyText, out var parsed))
            {
                StatusText.Text = S.BadHotkey;
                return;
            }
            var clash = _profiles.Data.Profiles.FirstOrDefault(p =>
                p.Key != _currentId &&
                Hotkey.TryParse(p.Value.Hotkey, out var other) && other == parsed);
            if (clash.Value != null)
            {
                StatusText.Text = S.HotkeyInUse(clash.Value.Name);
                return;
            }
            if (Hotkey.TryParse(_profiles.Data.MicMuteHotkey, out var mic) && mic == parsed)
            {
                StatusText.Text = S.MicHotkeyInUse;
                return;
            }
            hotkey = hotkeyText;
        }

        _draft.Name = name;
        _draft.Hotkey = hotkey;
        _draft.Color = _selectedColor;
        _draft.Sound = _selectedSound == SoundPlayer.DefaultSound ? null : _selectedSound;
        _draft.AutoSwitchDevice = Read(AutoDeviceBox);
        _draft.Playback = Read(PlaybackBox);
        _draft.Communications = Read(CommunicationsBox);
        _draft.Microphone = Read(MicrophoneBox);

        var apps = new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in _rules)
        {
            _draft.Apps.TryGetValue(rule.Exe, out var existing);
            apps[rule.Exe] = new AppRule
            {
                Volume = (float)Math.Round(rule.Volume / 100.0, 2),
                Muted = rule.Muted,
                Output = existing?.Output,
            };
        }
        _draft.Apps = apps;

        var id = _currentId;
        _profiles.ReplaceProfile(id, _draft);
        RefreshList(id);
        StatusText.Text = id == _profiles.ActiveId ? S.SavedApplied : S.Saved;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_currentId == null) return;
        var id = _currentId;
        _profiles.SaveCurrentInto(id);
        LoadDevices();
        RefreshList(id);
        StatusText.Text = S.Captured;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var id = _profiles.CreateFromCurrent();
        LoadDevices();
        RefreshList(id);
        StatusText.Text = S.Created;
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DeleteCurrentAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("delete profile", ex);
        }
    }

    private async Task DeleteCurrentAsync()
    {
        if (_currentId == null || _draft == null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = S.DeleteConfirm(_draft.Name),
            Content = S.CantUndo,
            PrimaryButtonText = S.Delete,
            CloseButtonText = S.Cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (_profiles.DeleteProfile(_currentId))
        {
            _currentId = null;
            RefreshList(_profiles.ActiveId);
        }
    }

    private void AddApps_Click(object sender, RoutedEventArgs e)
    {
        int added = 0;
        foreach (var app in _audio.GetApps())
        {
            if (_rules.Any(r => string.Equals(r.Exe, app.Exe, StringComparison.OrdinalIgnoreCase))) continue;
            _rules.Add(new RuleItem(app.Exe, Math.Round(app.Volume * 100), app.Muted));
            added++;
        }
        StatusText.Text = added == 0 ? S.NoNewApps : S.AppsAdded(added);
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string exe })
        {
            var rule = _rules.FirstOrDefault(r => r.Exe == exe);
            if (rule != null) _rules.Remove(rule);
        }
    }

    private void RestoreToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _profiles.SetRestoreOnStartup(RestoreToggle.IsOn);
    }

    private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _profiles.SetPlaySound(SoundToggle.IsOn);
        if (SoundToggle.IsOn) SoundPlayer.Play(_selectedSound);
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        NameCounter.Text = $"{NameBox.Text.Length}/{Profile.MaxNameLength}";

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || DemoMode.IsOn) return; // demo mode never touches the real Run key
        try
        {
            Startup.SetEnabled(StartupToggle.IsOn);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            CrashLog.Write("start with Windows", ex);
            _loading = true;
            StartupToggle.IsOn = Startup.IsEnabled;
            _loading = false;
        }
    }

    // Saved as soon as a full shortcut is recorded (or cleared with Backspace).
    private void OnMicHotkeyRecorded(ShortcutBox box, string recorded)
    {
        var text = recorded.Trim();
        if (text == (_profiles.Data.MicMuteHotkey ?? "")) return;

        if (text.Length > 0)
        {
            if (!Hotkey.TryParse(text, out var parsed))
            {
                StatusText.Text = S.BadHotkey;
                return;
            }
            var clash = _profiles.Data.Profiles.Values.FirstOrDefault(p =>
                Hotkey.TryParse(p.Hotkey, out var other) && other == parsed);
            if (clash != null)
            {
                StatusText.Text = S.HotkeyInUse(clash.Name);
                return;
            }
        }

        // Empty string (not null) = disabled, so the default doesn't come back on the next load.
        _profiles.SetMicMuteHotkey(text);
        StatusText.Text = S.MicHotkeySaved;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not LanguageOption option || option.Code == S.Language) return;
        _profiles.SetLanguage(option.Code);
        ((App)Application.Current).Restart();
    }
}
