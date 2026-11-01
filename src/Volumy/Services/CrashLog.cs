namespace Volumy.Services;

/// <summary>
/// crash.log: unhandled exceptions. trace.log: startup steps and first-chance exceptions,
/// so a native crash (which skips .NET handlers) still shows how far the app got.
/// Both live next to the exe.
/// </summary>
internal static class CrashLog
{
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "crash.log");
    public static readonly string TracePath = Path.Combine(AppContext.BaseDirectory, "trace.log");

    private static readonly object Gate = new();
    private static int _firstChanceCount;

    public static void Write(string source, Exception? ex)
    {
        Append(FilePath, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        Trace($"UNHANDLED [{source}] {ex?.GetType().Name}: {ex?.Message}");
    }

    public static void StartTrace()
    {
        // Keep the previous run's trace: after a crash, that's the one that matters.
        try { File.Move(TracePath, Path.ChangeExtension(TracePath, ".prev.log"), overwrite: true); } catch { }
        Trace($"start {Environment.ProcessPath}");
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (Interlocked.Increment(ref _firstChanceCount) > 80) return;
            var ex = e.Exception;
            Trace($"first-chance {ex.GetType().FullName}: {ex.Message} (HResult 0x{ex.HResult:X8})");
        };
    }

    public static void Trace(string message) =>
        Append(TracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");

    private static void Append(string path, string text)
    {
        try
        {
            lock (Gate) File.AppendAllText(path, text);
        }
        catch
        {
            // Never let logging itself crash the app.
        }
    }
}
