using Microsoft.Win32;

namespace Volumy.Services;

/// <summary>
/// One-time move from the app's old name (AudioProfiles) to Volumy: copies the settings folder
/// (profiles + custom sounds) and moves the "start with Windows" entry. Never deletes the old data.
/// </summary>
internal static class LegacyMigration
{
    private const string OldName = "AudioProfiles";
    private const string NewName = "Volumy";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Run()
    {
        Guard.Run("migrate settings", MigrateSettings);
        Guard.Run("migrate startup", MigrateStartup);
    }

    private static void MigrateSettings()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var oldDir = Path.Combine(appData, OldName);
        var newDir = Path.Combine(appData, NewName);
        if (!File.Exists(Path.Combine(oldDir, "profiles.json")) || File.Exists(Path.Combine(newDir, "profiles.json"))) return;

        CopyDirectory(oldDir, newDir);

        // Custom sounds are stored as full paths inside profiles.json: point them at the new folder.
        var json = Path.Combine(newDir, "profiles.json");
        var text = File.ReadAllText(json);
        text = text.Replace(oldDir.Replace("\\", "\\\\"), newDir.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(json, text);
        CrashLog.Trace($"migrated settings from {oldDir}");
    }

    private static void MigrateStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(OldName) is not string) return;
        key.DeleteValue(OldName, throwOnMissingValue: false);
        Startup.SetEnabled(true); // re-create it under the new name, pointing at this exe
        CrashLog.Trace("migrated start-with-Windows entry");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var dir in Directory.GetDirectories(from))
        {
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
