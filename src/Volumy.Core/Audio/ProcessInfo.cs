using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Volumy.Core.Audio;

/// <summary>Resolves a PID to its executable. Works for most elevated processes too.</summary>
public static class ProcessInfo
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // PID -> exe name for a few seconds: a profile switch or a slider drag asks for the same PIDs many times.
    private static readonly Dictionary<int, (string? Exe, long Ticks)> ExeCache = new();
    private static readonly Dictionary<string, string> DisplayNameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly long CacheTicks = TimeSpan.FromSeconds(5).Ticks;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    public static string? GetExePath(int pid)
    {
        if (pid <= 0) return null;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Lower-case exe file name, e.g. "spotify.exe". Null if the process is gone.</summary>
    public static string? GetExeName(int pid)
    {
        if (pid <= 0) return null;
        long now = DateTime.UtcNow.Ticks;
        lock (ExeCache)
        {
            if (ExeCache.TryGetValue(pid, out var hit) && now - hit.Ticks < CacheTicks) return hit.Exe;
        }
        var exe = LookupExeName(pid);
        lock (ExeCache)
        {
            ExeCache[pid] = (exe, now);
        }
        return exe;
    }

    private static string? LookupExeName(int pid)
    {
        var path = GetExePath(pid);
        if (path != null) return Path.GetFileName(path).ToLowerInvariant();
        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.ProcessName + ".exe").ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string GetDisplayName(string? exePath, string exe)
    {
        var key = exePath ?? exe;
        lock (DisplayNameCache)
        {
            if (DisplayNameCache.TryGetValue(key, out var cached)) return cached;
        }
        var name = LookupDisplayName(exePath, exe);
        lock (DisplayNameCache)
        {
            DisplayNameCache[key] = name;
        }
        return name;
    }

    private static string LookupDisplayName(string? exePath, string exe)
    {
        if (exePath != null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch (FileNotFoundException)
            {
            }
        }
        var name = Path.GetFileNameWithoutExtension(exe);
        return name.Length == 0 ? exe : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
