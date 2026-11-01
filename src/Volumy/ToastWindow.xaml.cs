using Volumy.Interop;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace Volumy;

/// <summary>
/// Glass toast shown at the bottom-right when a profile changes. Never takes focus,
/// fades in, hides itself after a few seconds; click to dismiss early.
/// </summary>
public sealed partial class ToastWindow : Window
{
    private const int WidthDip = 360;
    private const int HeightDip = 80;
    private const int MarginDip = 12;

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _timer;
    private readonly DesktopAcrylicController _acrylic;
    private readonly SystemBackdropConfiguration _backdropConfig;
    private Storyboard? _running;
    private bool _allowClose;

    public ToastWindow()
    {
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }
        AppWindow.IsShownInSwitchers = false;
        Win32.SetRoundedCorners(_hwnd);

        // A normal SystemBackdrop falls back to a flat color when the window isn't active.
        // The toast is never active, so drive the acrylic ourselves and keep it "active".
        _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        _acrylic = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintColor = ColorHelper.FromArgb(0xFF, 0x14, 0x14, 0x18),
            TintOpacity = 0.55f,
            LuminosityOpacity = 0.85f,
            FallbackColor = ColorHelper.FromArgb(0xFF, 0x1E, 0x1E, 0x22),
        };
        _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdropConfig);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(2600);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Services.Guard.Run("toast timer", FadeOut);

        AppWindow.Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            AppWindow.Hide();
        };
    }

    /// <summary>The toast faded out. The app closes it to free memory and recreates it next time.</summary>
    public event Action? Hidden;

    public bool IsShowing => AppWindow.IsVisible && !_allowClose;

    private static readonly Microsoft.UI.Xaml.Media.FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Microsoft.UI.Xaml.Media.FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>Shows an icon glyph (e.g. speaker) instead of a letter, with an optional 0–100 level bar.</summary>
    public void ShowGlyph(string glyph, string title, string message, double? level = null)
    {
        Show(glyph, title, message, null, isGlyph: true, level: level);
    }

    public void Show(string initial, string title, string message, string? color = null, bool isGlyph = false, double? level = null)
    {
        if (_allowClose) return;
        InitialCircle.Background = ViewModels.ProfileColors.Brush(color);
        InitialText.FontFamily = isGlyph ? SymbolFont : TextFont;
        InitialText.FontSize = isGlyph ? 19 : 17;
        InitialText.Text = initial;
        TitleText.Text = title;
        MessageText.Text = message;
        LevelBar.Visibility = level.HasValue ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Visibility = level.HasValue ? Visibility.Collapsed : Visibility.Visible;
        if (level.HasValue) LevelBar.Value = level.Value;

        double scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        int margin = (int)(MarginDip * scale);
        Win32.GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;

        AppWindow.ResizeClient(new SizeInt32((int)(WidthDip * scale), (int)(HeightDip * scale)));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - outer.Width - margin,
            area.Y + area.Height - outer.Height - margin));

        _timer.Stop();
        if (!AppWindow.IsVisible)
        {
            Root.Opacity = 0;
            AppWindow.Show(false); // false = don't steal focus from the app you're using
        }
        Fade(Root.Opacity, 1, 160, null);
        _timer.Start();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        _timer.Stop();
        _running?.Stop();
        Services.Guard.Run("toast backdrop", _acrylic.Dispose);
        Close();
    }

    private void FadeOut()
    {
        _timer.Stop();
        Fade(Root.Opacity, 0, 260, () =>
        {
            AppWindow.Hide();
            Hidden?.Invoke();
        });
    }

    private void Fade(double from, double to, int milliseconds, Action? done)
    {
        _running?.Stop();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, Root);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (_allowClose) return; // window is closing; nothing left to update
            Root.Opacity = to;
            if (_running == storyboard) _running = null;
            Services.Guard.Run("toast fade", () => done?.Invoke());
        };
        _running = storyboard;
        storyboard.Begin();
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e) => FadeOut();
}
