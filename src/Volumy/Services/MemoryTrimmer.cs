using System.Runtime;
using System.Runtime.InteropServices;

namespace Volumy.Services;

/// <summary>
/// A tray app spends almost all its time idle. After the flyout or toast closes, give unused
/// memory back to Windows: collect, compact, and trim the working set. Pages the app touches
/// again come back on demand, so this costs nothing while idle.
/// </summary>
internal static class MemoryTrimmer
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private static Func<bool>? _isBusy;

    /// <summary>Call once on the UI thread. <paramref name="isBusy"/> says whether any window is open.</summary>
    public static void Initialize(Microsoft.UI.Dispatching.DispatcherQueue ui, Func<bool> isBusy)
    {
        _isBusy = isBusy;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Guard.Run("memory trim", Trim);
    }

    /// <summary>Trims ~1.5 s after the last call, and only once every window is closed.</summary>
    public static void TrimSoon()
    {
        if (_timer == null) return;
        _timer.Stop(); // restart the countdown: opening/closing quickly trims once, at the end
        _timer.Start();
    }

    // Runs on the UI thread (timer), so it can safely ask the windows whether they're open.
    private static void Trim()
    {
        if (_isBusy?.Invoke() == true) return; // something is on screen: trimming now would cause a stutter

        _ = Task.Run(() =>
        {
            try
            {
                long before = Environment.WorkingSet;
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: true);
                EmptyWorkingSet(GetCurrentProcess());
                CrashLog.Trace($"memory trimmed: {before / 1048576} MB -> {Environment.WorkingSet / 1048576} MB");
            }
            catch (Exception ex)
            {
                CrashLog.Trace($"memory trim failed: {ex.Message}");
            }
        });
    }
}
