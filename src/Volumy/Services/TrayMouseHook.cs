using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Volumy.Services;

/// <summary>
/// Tray icons don't get mouse-wheel or middle-click messages, so this watches the mouse with a
/// low-level hook and reacts only while the cursor is over our tray icon (same trick EarTrumpet uses).
///
/// The hook lives on its own thread with its own message loop: a low-level mouse hook runs for
/// every mouse event on the whole system, so if it shared the UI thread, any slow UI work would
/// make the user's cursor stutter, and Windows would eventually remove the hook. Every callback
/// catches all exceptions, since an exception escaping a native callback kills the process.
/// </summary>
internal sealed class TrayMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UId;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr HWnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);

    private readonly Guid _iconId;
    private readonly DispatcherQueue _ui;
    private readonly HookProc _proc; // Field keeps the delegate alive while the hook is installed.
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private IntPtr _hook;
    private Rect _rect;
    private long _rectTicks;

    /// <summary>Wheel turned over the icon (raised on the UI thread). Argument: notches (+ up, − down).</summary>
    public event Action<int>? Wheel;

    /// <summary>Middle button clicked on the icon (raised on the UI thread).</summary>
    public event Action? MiddleClick;

    /// <summary>Right button released on the icon (raised on the UI thread). Argument: cursor position.</summary>
    public event Action<int, int>? RightClick;

    /// <summary>
    /// Any left/right press anywhere on screen, raised on the UI thread, only while
    /// <see cref="WatchClicks"/> is on. Used to close the tray menu when clicking elsewhere.
    /// </summary>
    public event Action<int, int>? ClickAnywhere;

    public volatile bool WatchClicks;

    public TrayMouseHook(Guid iconId)
    {
        _iconId = iconId;
        _ui = DispatcherQueue.GetForCurrentThread();
        _proc = HookCallback;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "Tray mouse hook" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _ready.Dispose();
    }

    private void HookThread()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                CrashLog.Trace($"tray mouse hook not installed (error {Marshal.GetLastWin32Error()})");
            }
            _ready.Set();

            // The hook is called from inside GetMessage on this thread.
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("tray mouse hook thread", ex);
        }
        finally
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _ready.Set();
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                int message = wParam.ToInt32();
                if (WatchClicks && message is WmLButtonDown or WmRButtonDown)
                {
                    var click = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                    _ui.TryEnqueue(() => Guard.Run("click anywhere", () => ClickAnywhere?.Invoke(click.Pt.X, click.Pt.Y)));
                }
                if (message == WmRButtonUp)
                {
                    var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                    if (IsOverIcon(data.Pt))
                    {
                        // Not swallowed: the shell still notifies the tray icon, which lets our menu take focus.
                        _ui.TryEnqueue(() => Guard.Run("tray right click", () => RightClick?.Invoke(data.Pt.X, data.Pt.Y)));
                    }
                }
                else if (message is WmMouseWheel or WmMButtonUp or WmMButtonDown)
                {
                    var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                    if (IsOverIcon(data.Pt))
                    {
                        if (message == WmMouseWheel)
                        {
                            int notches = (short)(data.MouseData >> 16) / 120;
                            if (notches != 0) _ui.TryEnqueue(() => Guard.Run("tray wheel", () => Wheel?.Invoke(notches)));
                        }
                        else if (message == WmMButtonUp)
                        {
                            _ui.TryEnqueue(() => Guard.Run("tray middle click", () => MiddleClick?.Invoke()));
                        }
                        return (IntPtr)1; // Swallow it so the taskbar doesn't also react.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("tray mouse hook", ex);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private bool IsOverIcon(Point pt)
    {
        // The icon can move (taskbar resized, overflow opened); re-read its position at most twice a second.
        long now = Environment.TickCount64;
        if (now - _rectTicks > 500)
        {
            var id = new NotifyIconIdentifier
            {
                CbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                GuidItem = _iconId,
            };
            _rect = Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : default;
            _rectTicks = now;
        }
        return pt.X >= _rect.Left && pt.X < _rect.Right && pt.Y >= _rect.Top && pt.Y < _rect.Bottom;
    }
}
