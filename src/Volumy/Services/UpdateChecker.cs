using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Dispatching;

namespace Volumy.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and installs it.
/// Installed copies download the new Setup and run it silently (it relaunches Volumy);
/// portable copies just open the release page.
/// </summary>
internal sealed class UpdateChecker
{
    private const string LatestUrl = "https://api.github.com/repos/szvieira/volumy/releases/latest";
    public const string ReleasesPage = "https://github.com/szvieira/volumy/releases/latest";

    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private readonly DispatcherQueue _ui;
    private readonly Func<bool> _enabled;
    private DispatcherQueueTimer? _timer;
    private bool _checking;

    public UpdateChecker(DispatcherQueue ui, Func<bool> enabled)
    {
        _ui = ui;
        _enabled = enabled;
    }

    public static Version Current { get; } =
        typeof(UpdateChecker).Assembly.GetName().Version is Version v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(1, 0, 0);

    /// <summary>Newer version found (null if up to date or not checked yet).</summary>
    public Version? Available { get; private set; }

    public string? PageUrl { get; private set; }

    private string? _setupUrl;

    /// <summary>Raised on the UI thread the first time a given newer version is found.</summary>
    public event Action<Version>? UpdateFound;

    /// <summary>Installed by the Setup (has an uninstaller next to it), as opposed to portable or a dev build.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public void Start()
    {
        _timer = _ui.CreateTimer();
        _timer.Interval = FirstCheck;
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Interval;
            if (_enabled()) _ = CheckAsync();
        };
        _timer.Start();
    }

    public async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            using var http = NewClient();
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(LatestUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return;
            latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
            if (latest <= Current) return;

            string? setup = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.StartsWith("Volumy-Setup-", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        setup = asset.GetProperty("browser_download_url").GetString();
                    }
                }
            }
            var page = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;

            _ui.TryEnqueue(() => Guard.Run("update found", () =>
            {
                bool isNew = Available != latest;
                Available = latest;
                _setupUrl = setup;
                PageUrl = page ?? ReleasesPage;
                CrashLog.Trace($"update: {Current} -> {latest}");
                if (isNew) UpdateFound?.Invoke(latest);
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or IOException)
        {
            // Offline, rate-limited or GitHub changed something: try again next time.
            CrashLog.Trace($"update check: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>
    /// Downloads the new Setup and starts it silently. Returns true when the Setup is running
    /// (the caller should quit so the files can be replaced). Portable copies open the release page instead.
    /// </summary>
    public async Task<bool> InstallAsync()
    {
        if (!IsInstalled || _setupUrl == null || Available == null)
        {
            OpenPage();
            return false;
        }

        var folder = Path.Combine(Path.GetTempPath(), "Volumy-Update");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"Volumy-Setup-{Available}.exe");

        using (var http = NewClient())
        {
            http.Timeout = TimeSpan.FromMinutes(10);
            using var response = await http.GetAsync(_setupUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(file);
            await source.CopyToAsync(target);
        }

        if (new FileInfo(file).Length < 1_000_000) throw new IOException("Downloaded setup is too small.");

        // Keep the user's current choices: the Setup would otherwise re-add "start with Windows".
        var tasks = new List<string>();
        if (Startup.IsEnabled) tasks.Add("startup");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Volumy.lnk");
        if (File.Exists(desktopLink)) tasks.Add("desktopicon");

        Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /TASKS=\"{string.Join(",", tasks)}\"",
            UseShellExecute = true,
        });
        CrashLog.Trace($"update: started setup {Available}");
        return true;
    }

    public void OpenPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PageUrl ?? ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashLog.Trace($"open release page: {ex.Message}");
        }
    }

    private static HttpClient NewClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Volumy/{Current}");
        return http;
    }
}
