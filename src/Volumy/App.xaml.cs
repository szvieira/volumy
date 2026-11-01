using System.Diagnostics;
using Volumy.Core.Audio;
using Volumy.Core.Profiles;
using Volumy.Localization;
using Volumy.Services;
using Volumy.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Volumy;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private DispatcherQueue _ui = null!;
    private AudioService _audio = null!;
    private ProfileManager _profiles = null!;
    private FlyoutWindow _flyout = null!;
    private HotkeyManager _hotkeys = null!;
    private TaskbarIcon _tray = null!;
    private readonly TrayIconFactory _trayIcons = new();
    private ProfilesWindow? _editor;
    private ToastWindow? _toast; // created on demand, closed after it fades out (saves ~15–25 MB idle)
    private TrayMouseHook? _trayHook;
    private const string MicHotkeyId = "__mic";
    private bool _started;
    private int _devicesRefreshPending;

    public App()
    {
        CrashLog.StartTrace();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write("Task", e.Exception);
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            CrashLog.Write("App.xaml", ex);
            throw;
        }
        // A bug in one button or timer shouldn't close a tray app: log it and keep running.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        Volumy.Core.Diagnostics.CoreLog.Sink = CrashLog.Trace;
        CrashLog.Trace("App.xaml loaded");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup", ex);
            throw;
        }
    }

    private void Launch()
    {
        _singleInstance = new Mutex(true, "Volumy.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Exit();
            return;
        }

        if (DemoMode.IsOn) CrashLog.Trace("demo mode");
        else LegacyMigration.Run(); // before anything reads %APPDATA%\Volumy
        CrashLog.Trace("audio service");
        _ui = DispatcherQueue.GetForCurrentThread();
        _audio = new AudioService();
        MemoryTrimmer.Initialize(_ui, () =>
            (_flyout?.IsOpen ?? false) || (_menu?.IsOpen ?? false) || _editor != null || (_toast?.IsShowing ?? false));
        CrashLog.Trace("profiles");
        _profiles = new ProfileManager(_audio, DemoMode.IsOn ? DemoMode.CreateStore() : new ProfileStore(), S.ProfileN);
        // Must be set before any window is created: XAML reads the strings once.
        S.SetLanguage(_profiles.Data.Language);
        CrashLog.Trace("flyout window");
        _flyout = new FlyoutWindow(_audio, _profiles);
        CrashLog.Trace("hotkeys");
        _hotkeys = new HotkeyManager(_flyout.Hwnd);

        _hotkeys.Pressed += Guard.Wrap<string>("hotkey", id =>
        {
            if (id == MicHotkeyId) ToggleMic();
            else _profiles.Apply(id);
        });
        _flyout.ViewModel.InputMuteChanged += Guard.Wrap("mic badge", UpdateTrayIcon);
        _flyout.EditRequested += Guard.Wrap("open editor", OpenEditor);

        // Core Audio events arrive on worker threads.
        // One profile switch fires several device events (one per role); coalesce them into one refresh.
        _audio.DevicesChanged += () =>
        {
            if (Interlocked.Exchange(ref _devicesRefreshPending, 1) == 1) return;
            _ui.TryEnqueue(DispatcherQueuePriority.Low, () => Guard.Run("devices changed", () =>
            {
                Interlocked.Exchange(ref _devicesRefreshPending, 0);
                CrashLog.Trace("devices changed");
                _audio.WatchSessions();
                UpdateTrayIcon(); // the default mic may have changed, and with it the badge
                if (_flyout.IsOpen) _flyout.ViewModel.RefreshDevices();
            }));
        };
        _audio.DeviceActivated += id => _ui.TryEnqueue(() => Guard.Run("device plugged in", () => _profiles.OnDeviceActivated(id)));
        _audio.AppSessionStarted += exe => _ui.TryEnqueue(() => Guard.Run("app started audio", () =>
        {
            _profiles.OnAppSessionStarted(exe);
            if (_flyout.IsOpen)
            {
                _flyout.ViewModel.RefreshApps();
                _flyout.FitToContent();
            }
        }));

        _profiles.ProfileApplied += Guard.Wrap<string>("profile applied", OnProfileApplied);
        _profiles.ProfilesChanged += Guard.Wrap("profiles changed", OnProfilesChanged);

        CrashLog.Trace("tray");
        CreateTray();
        CrashLog.Trace("register hotkeys");
        Guard.Run("register hotkeys", RegisterHotkeys);
        if (!DemoMode.IsOn) Guard.Run("start with Windows", Startup.RefreshPath);

        CrashLog.Trace("restore profile");
        if (_profiles.Data.RestoreOnStartup && _profiles.ActiveId is string active)
        {
            Guard.Run("restore profile", () => _profiles.Apply(active));
        }
        CrashLog.Trace("started");
        _started = true;
        MemoryTrimmer.TrimSoon(); // startup allocates a lot it never needs again
    }

    private void OnProfileApplied(string id)
    {
        CrashLog.Trace($"profile applied: {id}");
        UpdateTrayIcon();
        _flyout.ViewModel.Refresh();

        if (!_started || !_profiles.Data.Profiles.TryGetValue(id, out var profile)) return;
        var output = profile.Playback?.Name ?? S.DefaultOutput;
        var mic = profile.Microphone?.Name ?? S.DefaultMicrophone;
        // The flyout already shows the switch when it is open.
        if (!_flyout.IsOpen) Toast().Show(profile.Initial, S.ProfileActive(profile.Name), $"{output} · {mic}", profile.Color);

        // Plays on the new default output, so it also confirms where the sound is going now.
        if (_profiles.Data.PlaySound) SoundPlayer.Play(profile.Sound);
    }

    private void OnProfilesChanged()
    {
        RegisterHotkeys();
        UpdateTrayIcon();
        _flyout.ViewModel.Refresh();
    }

    // ---------- Tray ----------

    private void CreateTray()
    {
        _tray = new TaskbarIcon
        {
            NoLeftClickDelay = true,
            // No ContextFlyout: the right-click menu is our own TrayMenuWindow (see ShowTrayMenu).
            // H.NotifyIcon's SecondWindow menu mode crashed with "callback on a garbage collected delegate".
            LeftClickCommand = new RelayCommand(() => { CrashLog.Trace("tray: left click"); _flyout.Toggle(); }),
        };
        UpdateTrayIcon();
        CrashLog.Trace("tray create");
        _tray.ForceCreate(false);

        CrashLog.Trace("tray mouse hook");
        // Optional feature: if the hook can't start, the app still works without tray scrolling.
        Guard.Run("tray mouse hook", () =>
        {
            _trayHook = new TrayMouseHook(_tray.Id);
            _trayHook.Wheel += ChangeVolume;
            _trayHook.MiddleClick += ToggleOutputMute;
            // Open a moment after the click: the taskbar is still handling the mouse-up and would
            // otherwise take focus straight back and close the menu.
            _menuTimer = _ui.CreateTimer(); // kept in a field so it can't be collected before it fires
            _menuTimer.Interval = TimeSpan.FromMilliseconds(120);
            _menuTimer.IsRepeating = false;
            _menuTimer.Tick += (_, _) => Guard.Run("tray menu", () => ShowTrayMenu(_menuX, _menuY));
            _trayHook.RightClick += (x, y) =>
            {
                (_menuX, _menuY) = (x, y);
                _menuTimer.Stop();
                _menuTimer.Start();
            };
            _trayHook.ClickAnywhere += OnClickAnywhere;
        });
    }

    private void UpdateTrayIcon()
    {
        if (_tray == null) return;
        var active = _profiles.Active;
        bool micMuted = IsMicMuted();
        _tray.Icon = _trayIcons.Create(active?.Initial ?? "?", active?.Color, micMuted);
        var title = active != null ? $"{S.AppName} · {active.Name}" : S.AppName;
        if (micMuted) title += $" · {S.MicOffTag}";
        _tray.ToolTipText = $"{title}\n{S.TrayHint}";
    }

    // ---------- Tray menu (our own window; H.NotifyIcon's SecondWindow menu crashed the app) ----------

    private TrayMenuWindow? _menu;
    private DispatcherQueueTimer? _menuTimer;
    private int _menuX, _menuY;

    private void ShowTrayMenu(int x, int y)
    {
        CrashLog.Trace("tray: right click");
        if (_flyout.IsOpen) _flyout.HideFlyout();

        var items = new List<TrayMenuItem>();
        foreach (var (id, profile) in _profiles.Data.Profiles)
        {
            var profileId = id;
            items.Add(new TrayMenuItem(
                "\uE91F",
                profile.Name,
                () => _profiles.Apply(profileId),
                Shortcut: string.IsNullOrEmpty(profile.Hotkey) ? null : profile.Hotkey,
                GlyphBrush: ProfileColors.Brush(profile.Color),
                IsChecked: id == _profiles.ActiveId));
        }
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("\uE74E", S.MenuSaveToActive, () => _flyout.ViewModel.SaveIntoActive()));
        items.Add(new TrayMenuItem("\uE710", S.NewProfileFromCurrent, () => _profiles.Apply(_profiles.CreateFromCurrent())));
        items.Add(new TrayMenuItem("\uE713", S.MenuConfigure, OpenEditor));
        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("\uE8A5", S.MenuOpenJson, OpenJson));
        items.Add(new TrayMenuItem("\uE72C", S.MenuReload, () => _profiles.Reload()));
        items.Add(TrayMenuItem.Separator);
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        items.Add(new TrayMenuItem("", $"{S.AppName} {version}", null));
        items.Add(new TrayMenuItem("\uE7E8", S.MenuQuit, Quit));

        _menu ??= new TrayMenuWindow();
        _menu.ShowAt(items, x, y);
        if (_trayHook != null) _trayHook.WatchClicks = true;
    }

    /// <summary>Closes the menu when the user clicks anywhere outside it (works even if it never got focus).</summary>
    private void OnClickAnywhere(int x, int y)
    {
        if (_menu == null || !_menu.IsOpen)
        {
            if (_trayHook != null) _trayHook.WatchClicks = false;
            return;
        }
        var r = _menu.Bounds;
        bool inside = x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;
        if (!inside)
        {
            _menu.HideMenu();
            if (_trayHook != null) _trayHook.WatchClicks = false;
        }
    }

    private void RegisterHotkeys()
    {
        var bindings = _profiles.Data.Profiles.Select(p => (p.Key, p.Value.Hotkey)).ToList();
        bindings.Add((MicHotkeyId, _profiles.Data.MicMuteHotkey));
        var failed = _hotkeys.RegisterAll(bindings);
        if (failed.Count > 0)
        {
            Toast().Show("!", S.HotkeyUnavailable, S.HotkeyTaken(string.Join(", ", failed)));
        }
    }

    // ---------- Volume and mic shortcuts ----------

    private string? DefaultOutput() =>
        _audio.GetDefaultId(DeviceFlow.Playback, DeviceRole.Multimedia);

    private string? DefaultMic() =>
        _audio.GetDefaultId(DeviceFlow.Capture, DeviceRole.Communications)
        ?? _audio.GetDefaultId(DeviceFlow.Capture, DeviceRole.Multimedia);

    private string DeviceName(string id, DeviceFlow flow) =>
        _audio.GetDevices(flow).FirstOrDefault(d => d.Id == id)?.Name ?? "";

    private bool IsMicMuted() => DefaultMic() is string mic && (_audio.GetDeviceMute(mic) ?? false);

    /// <summary>Mouse wheel over the tray icon: 2% per notch on the default output.</summary>
    private void ChangeVolume(int notches)
    {
        CrashLog.Trace($"tray: wheel {notches}");
        if (DefaultOutput() is not string id) return;
        float volume = Math.Clamp((_audio.GetDeviceVolume(id) ?? 0f) + notches * 0.02f, 0f, 1f);
        _audio.SetDeviceVolume(id, volume);
        if (notches > 0 && _audio.GetDeviceMute(id) == true) _audio.SetDeviceMute(id, false);

        int percent = (int)Math.Round(volume * 100);
        Toast().ShowGlyph(percent == 0 ? Glyphs.Muted : Glyphs.Speaker, S.Volume(percent), "", percent);
        if (_flyout.IsOpen) _flyout.ViewModel.RefreshDevices();
    }

    /// <summary>Middle-click on the tray icon: mute/unmute the default output.</summary>
    private void ToggleOutputMute()
    {
        CrashLog.Trace("tray: middle click");
        if (DefaultOutput() is not string id) return;
        bool muted = !(_audio.GetDeviceMute(id) ?? false);
        _audio.SetDeviceMute(id, muted);

        int percent = (int)Math.Round((_audio.GetDeviceVolume(id) ?? 0f) * 100);
        if (muted) Toast().ShowGlyph(Glyphs.Muted, S.SoundMuted, DeviceName(id, DeviceFlow.Playback));
        else Toast().ShowGlyph(Glyphs.Speaker, S.Volume(percent), "", percent);
        if (_flyout.IsOpen) _flyout.ViewModel.RefreshDevices();
    }

    /// <summary>Mic-mute shortcut: toggles the default microphone(s) and updates the tray badge.</summary>
    private void ToggleMic()
    {
        CrashLog.Trace("mic shortcut");
        if (DefaultMic() is not string id) return;
        bool muted = !(_audio.GetDeviceMute(id) ?? false);
        _audio.SetDeviceMute(id, muted);
        // If the "default" and "communications" mics differ, keep them in sync.
        if (_audio.GetDefaultId(DeviceFlow.Capture, DeviceRole.Multimedia) is string other && other != id)
        {
            _audio.SetDeviceMute(other, muted);
        }

        UpdateTrayIcon();
        Toast().ShowGlyph(muted ? Glyphs.MicOff : Glyphs.Mic, muted ? S.MicMuted : S.MicOn, DeviceName(id, DeviceFlow.Capture));
        if (_flyout.IsOpen) _flyout.ViewModel.RefreshDevices();
    }

    /// <summary>Returns the toast window, creating it if it was closed.</summary>
    private ToastWindow Toast()
    {
        if (_toast == null)
        {
            CrashLog.Trace("toast: create");
            var toast = new ToastWindow();
            toast.Hidden += () => _ui.TryEnqueue(DispatcherQueuePriority.Low, () => Guard.Run("toast: close", () =>
            {
                // Only close if nothing re-showed it in the meantime.
                if (_toast != toast || toast.IsShowing) return;
                CrashLog.Trace("toast: close");
                _toast = null;
                toast.CloseForExit();
                MemoryTrimmer.TrimSoon();
            }));
            _toast = toast;
        }
        return _toast;
    }

    private void OpenEditor()
    {
        if (_editor == null)
        {
            _editor = new ProfilesWindow(_audio, _profiles);
            _editor.Closed += (_, _) => _editor = null;
        }
        _editor.Activate();
    }

    private void OpenJson()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_profiles.StorePath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No app is associated with .json: open it in Notepad instead.
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_profiles.StorePath}\"") { UseShellExecute = true });
        }
    }

    /// <summary>Restarts the app (used after changing the language).</summary>
    public void Restart() => Quit(relaunch: true);

    private void Quit() => Quit(relaunch: false);

    private void Quit(bool relaunch)
    {
        // Each step on its own: one failing cleanup must not stop the app from exiting.
        Guard.Run("quit: editor", () => _editor?.Close());
        Guard.Run("quit: menu", () => _menu?.CloseForExit());
        Guard.Run("quit: flyout", () => _flyout.CloseForExit());
        Guard.Run("quit: toast", () => _toast?.CloseForExit());
        Guard.Run("quit: tray hook", () => _trayHook?.Dispose());
        Guard.Run("quit: hotkeys", () => _hotkeys.Dispose());
        Guard.Run("quit: tray", () => _tray.Dispose());
        Guard.Run("quit: tray icons", _trayIcons.Dispose);
        Guard.Run("quit: audio", _audio.Dispose);
        Guard.Run("quit: mutex", () =>
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        });
        if (relaunch && Environment.ProcessPath is string exe)
        {
            Guard.Run("restart", () => Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }));
        }
        Exit();
    }
}
