using Windows.Media.Core;
using Windows.Media.Playback;

namespace Volumy.Services;

/// <summary>
/// Plays profile switch sounds on the current default output.
/// A sound reference is "builtin:&lt;id&gt;" or an absolute path to a user file; null means the default.
/// MediaPlayer handles WAV, MP3, M4A/AAC, WMA, FLAC, and OGG when the codec is installed.
/// </summary>
public static class SoundPlayer
{
    public const string DefaultSound = "builtin:chime";

    public static readonly string[] BuiltIns = { "chime", "bubble", "pulse", "glass", "blip", "warp", "droplet", "orbit" };

    public static readonly string[] FileTypes = { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac", ".ogg" };

    /// <summary>Folder where user-picked sounds are copied, so they survive the original being moved.</summary>
    public static readonly string CustomFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Volumy", "sounds");

    private static MediaPlayer? _player;

    public static bool IsBuiltIn(string? sound, out string id)
    {
        id = "";
        if (sound == null || !sound.StartsWith("builtin:", StringComparison.Ordinal)) return false;
        id = sound["builtin:".Length..];
        return BuiltIns.Contains(id);
    }

    public static string? ResolvePath(string? sound)
    {
        if (IsBuiltIn(sound ?? DefaultSound, out var id))
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", id + ".wav");
        }
        if (sound != null && File.Exists(sound)) return sound;
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "chime.wav");
    }

    public static void Play(string? sound)
    {
        var path = ResolvePath(sound);
        if (path == null || !File.Exists(path)) return;
        try
        {
            lock (Gate)
            {
                // A player only lives while a sound plays (Media Foundation holds a few MB); see Release().
                if (_player == null)
                {
                    _player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.SoundEffects };
                    _player.MediaEnded += (sender, _) => Release(sender);
                    _player.MediaFailed += (sender, _) => Release(sender);
                }
                _player.Pause();
                _player.Source = MediaSource.CreateFromUri(new Uri(path));
                _player.Play();
            }
        }
        catch (Exception ex)
        {
            // A sound that can't play must never break a profile switch.
            CrashLog.Trace($"sound: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static readonly object Gate = new();

    // Raised on a media thread when the sound ends. MediaPlayer is thread-agile, so dispose it here.
    private static void Release(object sender)
    {
        try
        {
            lock (Gate)
            {
                if (!ReferenceEquals(sender, _player)) return; // a newer sound already replaced it
                _player!.Dispose();
                _player = null;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Trace($"sound release: {ex.Message}");
        }
    }

    /// <summary>Copies a user file into the app's sounds folder and returns the new path.</summary>
    public static string ImportCustom(string sourcePath)
    {
        Directory.CreateDirectory(CustomFolder);
        var target = Path.Combine(CustomFolder, Path.GetFileName(sourcePath));
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, target, overwrite: true);
        }
        return target;
    }
}
