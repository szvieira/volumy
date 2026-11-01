using System.Runtime.InteropServices;

namespace Volumy.Interop;

internal static class Win32
{
    public const uint WmHotkey = 0x0312;

    public delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr idSubclass, IntPtr refData);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr idSubclass, IntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr idSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    /// <summary>Plays a WAV file on the current default output without blocking.</summary>
    public static void PlayWav(string path)
    {
        if (File.Exists(path)) PlaySound(path, IntPtr.Zero, SndAsync | SndFilename | SndNoDefault);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    /// <summary>Windows 11 rounded corners (no-op on Windows 10).</summary>
    public static void SetRoundedCorners(IntPtr hWnd)
    {
        const int DwmwaWindowCornerPreference = 33;
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hWnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }
}
