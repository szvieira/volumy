using System.Text.Json;
using System.Text.Json.Serialization;
using Volumy.Core.Diagnostics;

namespace Volumy.Core.Profiles;

/// <summary>
/// Reads and writes %APPDATA%\Volumy\profiles.json.
///
/// Error policy: never throws. A broken file is kept as a .broken copy and the app starts clean;
/// a locked file (antivirus, OneDrive) is retried; if the file can't be read at all, saving is
/// disabled for this run so the user's profiles are never overwritten with an empty set.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private bool _readOnly;

    public string FilePath { get; }

    /// <summary>The last save failed (or saving is disabled). The UI can warn about it.</summary>
    public bool LastSaveFailed { get; private set; }

    public ProfileStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Volumy",
            "profiles.json");
    }

    public ProfileFile Load()
    {
        if (!File.Exists(FilePath)) return new ProfileFile();

        string? json = Retry("read profiles", () => File.ReadAllText(FilePath));
        if (json == null)
        {
            // Exists but unreadable: work in memory and don't overwrite it.
            _readOnly = true;
            return new ProfileFile();
        }

        try
        {
            var data = JsonSerializer.Deserialize<ProfileFile>(json, Options) ?? new ProfileFile();
            return Sanitize(data);
        }
        catch (JsonException ex)
        {
            CoreLog.Recovered("parse profiles.json", ex);
            Retry("back up broken profiles", () =>
            {
                File.Copy(FilePath, FilePath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
                return true;
            });
            return new ProfileFile();
        }
    }

    /// <summary>Saves atomically (temp file + rename). Returns false instead of throwing.</summary>
    public bool Save(ProfileFile data)
    {
        if (_readOnly)
        {
            LastSaveFailed = true;
            return false;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(data, Options);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            CoreLog.Recovered("serialize profiles", ex);
            LastSaveFailed = true;
            return false;
        }

        bool ok = Retry("save profiles", () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
            return true;
        });
        LastSaveFailed = !ok;
        return ok;
    }

    public static Profile Clone(Profile profile)
    {
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, Options), Options) ?? new Profile();
        return SanitizeProfile(copy);
    }

    /// <summary>
    /// Hand-edited JSON can contain nulls anywhere ("apps": null, a null profile...).
    /// Fill in defaults so the rest of the app never sees a null collection.
    /// </summary>
    private static ProfileFile Sanitize(ProfileFile data)
    {
        var clean = new Dictionary<string, Profile>();
        foreach (var (id, profile) in data.Profiles ?? new Dictionary<string, Profile>())
        {
            if (string.IsNullOrWhiteSpace(id) || profile == null) continue;
            clean[id] = SanitizeProfile(profile);
        }
        data.Profiles = clean;
        data.Language ??= "en";
        return data;
    }

    private static Profile SanitizeProfile(Profile profile)
    {
        profile.Name = (profile.Name ?? "").Trim();
        if (profile.Name.Length > Profile.MaxNameLength) profile.Name = profile.Name[..Profile.MaxNameLength].TrimEnd();
        profile.Devices = (profile.Devices ?? new List<DeviceSetting>())
            .Where(d => d != null && !string.IsNullOrEmpty(d.Id))
            .Select(d =>
            {
                d.Name ??= "";
                d.Volume = float.IsFinite(d.Volume) ? Math.Clamp(d.Volume, 0f, 1f) : 1f;
                return d;
            })
            .ToList();

        var apps = new Dictionary<string, AppRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (exe, rule) in profile.Apps ?? new Dictionary<string, AppRule>())
        {
            if (string.IsNullOrWhiteSpace(exe) || rule == null) continue;
            if (rule.Volume is float v) rule.Volume = float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : null;
            apps[exe] = rule;
        }
        profile.Apps = apps;

        profile.Playback = CleanRef(profile.Playback);
        profile.Communications = CleanRef(profile.Communications);
        profile.Microphone = CleanRef(profile.Microphone);
        profile.AutoSwitchDevice = CleanRef(profile.AutoSwitchDevice);
        return profile;
    }

    private static DeviceRef? CleanRef(DeviceRef? reference)
    {
        if (reference == null || (string.IsNullOrEmpty(reference.Id) && string.IsNullOrEmpty(reference.Name))) return null;
        reference.Id ??= "";
        reference.Name ??= "";
        return reference;
    }

    /// <summary>Files can be briefly locked by antivirus or sync tools: try a few times.</summary>
    private static T? Retry<T>(string what, Func<T> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 3)
                {
                    CoreLog.Recovered(what, ex);
                    return default;
                }
                Thread.Sleep(80 * attempt);
            }
        }
    }
}
