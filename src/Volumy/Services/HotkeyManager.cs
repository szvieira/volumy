using Volumy.Core.Hotkeys;
using Volumy.Interop;

namespace Volumy.Services;

/// <summary>Registers one global hotkey per profile on the flyout window's HWND.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Win32.SubclassProc _proc; // Kept in a field so the GC doesn't collect the delegate.
    private readonly Dictionary<int, string> _profileByHotkeyId = new();
    private int _nextId = 1;

    public event Action<string>? Pressed;

    public HotkeyManager(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WndProc;
        Win32.SetWindowSubclass(_hwnd, _proc, (UIntPtr)1, IntPtr.Zero);
    }

    /// <summary>Returns the hotkeys that could not be registered (invalid or used by another app).</summary>
    public IReadOnlyList<string> RegisterAll(IEnumerable<(string ProfileId, string? Hotkey)> bindings)
    {
        UnregisterAll();
        var failed = new List<string>();
        foreach (var (profileId, text) in bindings)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!Hotkey.TryParse(text, out var hotkey))
            {
                failed.Add(text);
                continue;
            }
            int id = _nextId++;
            if (Win32.RegisterHotKey(_hwnd, id, hotkey.Modifiers | Hotkey.ModNoRepeat, hotkey.VirtualKey))
            {
                _profileByHotkeyId[id] = profileId;
            }
            else
            {
                failed.Add(text);
            }
        }
        return failed;
    }

    public void Dispose()
    {
        UnregisterAll();
        Win32.RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)1);
    }

    private void UnregisterAll()
    {
        foreach (var id in _profileByHotkeyId.Keys)
        {
            Win32.UnregisterHotKey(_hwnd, id);
        }
        _profileByHotkeyId.Clear();
    }

    // Called directly by Windows for every message to the flyout window. An exception escaping a
    // native callback can't be caught anywhere else and kills the process, so catch everything here.
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr idSubclass, IntPtr refData)
    {
        if (msg == Win32.WmHotkey)
        {
            try
            {
                if (_profileByHotkeyId.TryGetValue(wParam.ToInt32(), out var profileId))
                {
                    Pressed?.Invoke(profileId);
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("hotkey", ex);
                return IntPtr.Zero;
            }
        }
        return Win32.DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
