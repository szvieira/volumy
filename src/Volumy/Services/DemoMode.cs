using Volumy.Core.Profiles;

namespace Volumy.Services;

/// <summary>
/// "Volumy.exe --demo": runs with a separate set of sample profiles (for README screenshots).
/// The demo profiles have no devices and no app rules, so switching between them
/// changes nothing on the system. The real profiles.json is never touched.
/// </summary>
internal static class DemoMode
{
    public static bool IsOn { get; } =
        Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Volumy", "demo-profiles.json");

    /// <summary>Store for demo mode; seeds the sample profiles the first time.</summary>
    public static ProfileStore CreateStore()
    {
        var store = new ProfileStore(FilePath);
        if (!File.Exists(FilePath))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                store.Save(Seed());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashLog.Trace($"demo seed: {ex.Message}");
            }
        }
        return store;
    }

    private static ProfileFile Seed()
    {
        var file = new ProfileFile
        {
            ActiveProfile = "alex",
            RestoreOnStartup = false,
            PlaySound = true,
            Language = "en",
            MicMuteHotkey = "Ctrl+Alt+M",
        };

        void Add(string id, string name, string color, string sound, string hotkey) =>
            file.Profiles[id] = new Profile
            {
                Name = name,
                Color = color,
                Sound = "builtin:" + sound,
                Hotkey = hotkey,
            };

        Add("alex", "Alex", "#7FB2FF", "chime", "Ctrl+Alt+1");
        Add("sam", "Sam", "#FF9CC8", "bubble", "Ctrl+Alt+2");
        Add("jordan", "Jordan", "#8BD17C", "glass", "Ctrl+Alt+3");
        Add("riley", "Riley", "#FFD166", "orbit", "Ctrl+Alt+4");
        return file;
    }
}
