namespace Volumy.Core.Diagnostics;

/// <summary>
/// The Core library has no UI or files of its own; it reports errors it recovered from here,
/// and the app forwards them to trace.log.
/// </summary>
public static class CoreLog
{
    /// <summary>Set by the app at startup.</summary>
    public static Action<string>? Sink { get; set; }

    public static void Recovered(string where, Exception ex)
    {
        try
        {
            Sink?.Invoke($"recovered in {where}: {ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8})");
        }
        catch
        {
            // Logging must never throw.
        }
    }

    /// <summary>
    /// Errors that are expected when talking to Windows audio: a device unplugged or a session
    /// ending mid-call, the audio service restarting, a COM object already released, etc.
    /// </summary>
    public static bool IsExpected(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException
            or System.Runtime.InteropServices.InvalidComObjectException
            or InvalidCastException
            or InvalidOperationException
            or ArgumentException
            or UnauthorizedAccessException
            or NotSupportedException
            or NullReferenceException
            or ObjectDisposedException;
}
