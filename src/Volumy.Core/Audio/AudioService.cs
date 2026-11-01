using Volumy.Core.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Volumy.Core.Audio;

/// <summary>
/// Thin layer over Windows Core Audio (via NAudio): devices, default endpoints,
/// device volume and per-app session volume.
///
/// Error policy: devices and sessions can disappear at any moment (unplugged, app closed,
/// audio service restarted), so every public method catches the expected COM errors,
/// logs them, and returns a neutral result (empty list, null, no-op) instead of throwing.
/// Events are raised on COM worker threads; callers must marshal to the UI thread.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notifications;
    private readonly List<MMDevice> _watchedDevices = new();
    private readonly List<MMDevice> _meterDevices = new();
    private bool _disposed;

    /// <summary>A device was added, removed, enabled/disabled, or a default changed.</summary>
    public event Action? DevicesChanged;

    /// <summary>A device became active (plugged in). Argument: device id.</summary>
    public event Action<string>? DeviceActivated;

    /// <summary>An app opened a new audio session. Argument: lower-case exe name.</summary>
    public event Action<string>? AppSessionStarted;

    public AudioService()
    {
        _notifications = new NotificationClient(this);
        Try("register notifications", () => _enumerator.RegisterEndpointNotificationCallback(_notifications));
        WatchSessions();
    }

    // ---------- Devices ----------

    public IReadOnlyList<AudioDevice> GetDevices(DeviceFlow flow)
    {
        var result = new List<AudioDevice>();
        Try("enumerate devices", () =>
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(ToDataFlow(flow), DeviceState.Active))
            {
                // Read each device on its own: one broken driver shouldn't hide the others.
                Try("read device", () => result.Add(new AudioDevice(device.ID, device.FriendlyName, flow)));
            }
        });
        return result;
    }

    public string? GetDefaultId(DeviceFlow flow, DeviceRole role) =>
        Get("default device", () =>
        {
            var dataFlow = ToDataFlow(flow);
            var nativeRole = (Role)(int)role;
            if (!_enumerator.HasDefaultAudioEndpoint(dataFlow, nativeRole)) return null;
            using var device = _enumerator.GetDefaultAudioEndpoint(dataFlow, nativeRole);
            return device.ID;
        });

    /// <summary>Sets the default device for each role. Returns false if Windows refused.</summary>
    public bool SetDefault(string deviceId, params DeviceRole[] roles)
    {
        bool ok = true;
        foreach (var role in roles)
        {
            ok &= Try($"set default ({role})", () => PolicyConfig.SetDefaultEndpoint(deviceId, role));
        }
        return ok;
    }

    public float? GetDeviceVolume(string deviceId) =>
        WithDevice(deviceId, "get volume", d => (float?)d.AudioEndpointVolume.MasterVolumeLevelScalar);

    public bool? GetDeviceMute(string deviceId) =>
        WithDevice(deviceId, "get mute", d => (bool?)d.AudioEndpointVolume.Mute);

    public void SetDeviceVolume(string deviceId, float volume) =>
        WithDevice(deviceId, "set volume", d =>
        {
            d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            return (bool?)true;
        });

    public void SetDeviceMute(string deviceId, bool muted) =>
        WithDevice(deviceId, "set mute", d =>
        {
            d.AudioEndpointVolume.Mute = muted;
            return (bool?)true;
        });

    // ---------- Apps (sessions) ----------

    public IReadOnlyList<AppAudio> GetApps()
    {
        var byExe = new Dictionary<string, AppAudio>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in Sessions())
        {
            Try("read session", () =>
            {
                int pid = (int)session.GetProcessID;
                var exe = ProcessInfo.GetExeName(pid);
                if (exe == null || byExe.ContainsKey(exe)) return;
                var path = ProcessInfo.GetExePath(pid);
                byExe[exe] = new AppAudio(
                    exe,
                    ProcessInfo.GetDisplayName(path, exe),
                    path,
                    session.SimpleAudioVolume.Volume,
                    session.SimpleAudioVolume.Mute);
            });
        }
        return byExe.Values
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Applies volume/mute rules to every session in a single pass.
    /// <paramref name="ruleFor"/> returns the rule for an exe, or null to leave it alone.
    /// </summary>
    public void ApplyAppRules(Func<string, (float? Volume, bool? Muted)?> ruleFor)
    {
        foreach (var session in Sessions())
        {
            Try("apply app rule", () =>
            {
                var exe = ProcessInfo.GetExeName((int)session.GetProcessID);
                if (exe == null || ruleFor(exe) is not { } rule) return;
                if (rule.Volume is float volume) session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                if (rule.Muted is bool muted) session.SimpleAudioVolume.Mute = muted;
            });
        }
    }

    public void SetAppVolume(string exe, float volume)
    {
        foreach (var session in SessionsFor(exe))
        {
            Try("set app volume", () => session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f));
        }
    }

    public void SetAppMute(string exe, bool muted)
    {
        foreach (var session in SessionsFor(exe))
        {
            Try("set app mute", () => session.SimpleAudioVolume.Mute = muted);
        }
    }

    // ---------- Level meters ----------

    /// <summary>Peak-level readers (0..1) for every app session, grouped by exe. Readers never throw.</summary>
    public Dictionary<string, List<Func<float>>> GetSessionMeters()
    {
        var result = new Dictionary<string, List<Func<float>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in Sessions())
        {
            Try("session meter", () =>
            {
                var exe = ProcessInfo.GetExeName((int)session.GetProcessID);
                if (exe == null) return;
                var meter = session.AudioMeterInformation;
                if (!result.TryGetValue(exe, out var list)) result[exe] = list = new List<Func<float>>();
                list.Add(() => SafePeak(() => meter.MasterPeakValue));
            });
        }
        return result;
    }

    /// <summary>Peak-level reader (0..1) for a device. Valid until <see cref="ReleaseDeviceMeters"/>. Never throws.</summary>
    public Func<float>? GetDeviceMeter(string deviceId) =>
        Get("device meter", () =>
        {
            var device = _enumerator.GetDevice(deviceId);
            _meterDevices.Add(device);
            var meter = device.AudioMeterInformation;
            return (Func<float>)(() => SafePeak(() => meter.MasterPeakValue));
        });

    public void ReleaseDeviceMeters()
    {
        foreach (var device in _meterDevices) Try("release meter", device.Dispose);
        _meterDevices.Clear();
    }

    private static float SafePeak(Func<float> read)
    {
        try
        {
            float value = read();
            return float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
        }
        catch (Exception ex) when (CoreLog.IsExpected(ex))
        {
            return 0f; // Session ended or device unplugged between refreshes; not worth logging 25×/s.
        }
    }

    // ---------- Session notifications ----------

    /// <summary>
    /// Subscribes to "new session" notifications on every active playback device.
    /// Call again (on the UI thread) whenever the device list changes.
    /// </summary>
    public void WatchSessions()
    {
        if (_disposed) return;
        foreach (var device in _watchedDevices)
        {
            Try("unwatch sessions", () =>
            {
                device.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
                device.Dispose();
            });
        }
        _watchedDevices.Clear();

        Try("watch sessions", () =>
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                Try("watch device", () =>
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions(); // Windows only sends notifications after one enumeration.
                    manager.OnSessionCreated += OnSessionCreated;
                    _watchedDevices.Add(device);
                });
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var device in _watchedDevices)
        {
            Try("dispose watch", () => device.AudioSessionManager.OnSessionCreated -= OnSessionCreated);
        }
        _watchedDevices.Clear();
        ReleaseDeviceMeters();
        Try("unregister notifications", () => _enumerator.UnregisterEndpointNotificationCallback(_notifications));
        Try("dispose enumerator", _enumerator.Dispose);
    }

    // ---------- Internals ----------

    /// <summary>
    /// Snapshot of the live sessions. Built eagerly inside try blocks, because enumerating
    /// (not just reading) can throw when a device disappears mid-loop.
    /// </summary>
    private List<AudioSessionControl> Sessions()
    {
        var result = new List<AudioSessionControl>();
        if (_disposed) return result;
        Try("enumerate sessions", () =>
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                Try("enumerate device sessions", () =>
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.IsSystemSoundsSession) continue;
                        if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                        result.Add(session);
                    }
                });
            }
        });
        return result;
    }

    private List<AudioSessionControl> SessionsFor(string exe) =>
        Sessions().Where(s =>
        {
            try
            {
                return string.Equals(ProcessInfo.GetExeName((int)s.GetProcessID), exe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (CoreLog.IsExpected(ex))
            {
                return false;
            }
        }).ToList();

    // Runs on a COM worker thread: an exception escaping here would kill the process.
    private void OnSessionCreated(object sender, IAudioSessionControl newSession)
    {
        try
        {
            var control = new AudioSessionControl(newSession);
            var exe = ProcessInfo.GetExeName((int)control.GetProcessID);
            if (exe != null) AppSessionStarted?.Invoke(exe);
        }
        catch (Exception ex)
        {
            CoreLog.Recovered("session created callback", ex);
        }
    }

    private T? WithDevice<T>(string deviceId, string what, Func<MMDevice, T?> action) =>
        Get(what, () =>
        {
            using var device = _enumerator.GetDevice(deviceId);
            return action(device);
        });

    private static bool Try(string what, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (CoreLog.IsExpected(ex))
        {
            CoreLog.Recovered(what, ex);
            return false;
        }
    }

    private static T? Get<T>(string what, Func<T?> func)
    {
        try
        {
            return func();
        }
        catch (Exception ex) when (CoreLog.IsExpected(ex))
        {
            CoreLog.Recovered(what, ex);
            return default;
        }
    }

    private static DataFlow ToDataFlow(DeviceFlow flow) =>
        flow == DeviceFlow.Playback ? DataFlow.Render : DataFlow.Capture;

    /// <summary>Called by Windows on a worker thread. Every callback is wrapped: an escaping exception kills the process.</summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly AudioService _owner;

        public NotificationClient(AudioService owner) => _owner = owner;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Safe(() =>
        {
            if (newState == DeviceState.Active) _owner.DeviceActivated?.Invoke(deviceId);
            _owner.DevicesChanged?.Invoke();
        });

        public void OnDeviceAdded(string pwstrDeviceId) => Safe(() => _owner.DevicesChanged?.Invoke());

        public void OnDeviceRemoved(string deviceId) => Safe(() => _owner.DevicesChanged?.Invoke());

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            Safe(() => _owner.DevicesChanged?.Invoke());

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }

        private static void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CoreLog.Recovered("device notification", ex);
            }
        }
    }
}
