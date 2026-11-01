using Microsoft.Win32;

namespace Volumy.Services;

/// <summary>"Start with Windows" through the per-user Run key (no admin rights needed).</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Volumy";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is string exe)
        {
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>If the exe moved (e.g. rebuilt elsewhere), point the Run entry at the current one.</summary>
    public static void RefreshPath()
    {
        if (IsEnabled) SetEnabled(true);
    }
}
