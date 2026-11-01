using Volumy.Core.Audio;
using Volumy.Core.Profiles;
using Volumy.Interop;
using Volumy.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace Volumy;

/// <summary>The glass flyout that opens above the tray icon.</summary>
public sealed partial class FlyoutWindow : Window
{
    private const int WidthDip = 380;
    private const int MaxHeightDip = 860;
    private const int MarginDip = 12;

    private static readonly string[] AccentKeys =
    {
        "SliderTrackValueFill",
        "SliderTrackValueFillPointerOver",
        "SliderTrackValueFillPressed",
    };

    private readonly ProfileManager _profiles;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _allowClose;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _meterTimer;
    private RectInt32 _area;

    public FlyoutViewModel ViewModel { get; }

    public IntPtr Hwnd { get; }

    public FlyoutWindow(AudioService audio, ProfileManager profiles)
    {
        _profiles = profiles;
        ViewModel = new FlyoutViewModel(audio, profiles);
        InitializeComponent();

        Hwnd = WindowNative.GetWindowHandle(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }
        AppWindow.IsShownInSwitchers = false;
        Win32.SetRoundedCorners(Hwnd);

        Activated += OnActivated;
        bool watching = false;
        Root.Loaded += (_, _) =>
        {
            if (watching || Root.XamlRoot == null) return;
            watching = true;
            Root.XamlRoot.Changed += (_, _) => Services.Guard.Run("flyout: trim gap", TrimBottomGap);
        };
        AppWindow.Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            HideFlyout();
        };
    }

    public bool IsOpen => AppWindow.IsVisible;

    public void Toggle()
    {
        if (IsOpen)
        {
            HideFlyout();
            return;
        }
        // Clicking the tray icon deactivates (hides) the flyout first; don't reopen it right away.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300) return;
        ShowFlyout();
    }

    public void ShowFlyout()
    {
        Services.CrashLog.Trace("flyout: show");
        ViewModel.Refresh();

        Win32.GetCursorPos(out var cursor);
        _area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        FitToContent();

        AppWindow.Show();
        StartMeters();
        Activate();
        Win32.SetForegroundWindow(Hwnd);
    }

    /// <summary>
    /// Sizes the window to its content: the apps list grows one row per app up to 5,
    /// then scrolls. Keeps the window anchored to the bottom-right corner.
    /// </summary>
    public void FitToContent()
    {
        double scale = Win32.GetDpiForWindow(Hwnd) / 96.0;
        int margin = (int)(MarginDip * scale);
        int width = (int)(WidthDip * scale);
        int maxHeight = Math.Min((int)(MaxHeightDip * scale), _area.Height - 2 * margin);

        // Give the content all the room it could want, let XAML lay it out (app rows included),
        // then shrink the window to the real height of the content, bottom padding included.
        AppWindow.ResizeClient(new SizeInt32(width, maxHeight));
        Root.UpdateLayout();
        ResizeToContent(Root.ActualHeight);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsOpen) ResizeToContent(e.NewSize.Height);
    }

    private void ResizeToContent(double contentHeightDip)
    {
        if (contentHeightDip <= 0) return;
        double scale = Win32.GetDpiForWindow(Hwnd) / 96.0;
        int margin = (int)(MarginDip * scale);
        int width = (int)(WidthDip * scale);
        int maxHeight = Math.Min((int)(MaxHeightDip * scale), _area.Height - 2 * margin);
        int height = Math.Min((int)Math.Ceiling(contentHeightDip * scale) + 1 - _gapPx, maxHeight);

        AppWindow.ResizeClient(new SizeInt32(width, height));
        var outer = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            _area.X + _area.Width - outer.Width - margin,
            _area.Y + _area.Height - outer.Height - margin));
    }

    /// <summary>
    /// Extra pixels Windows adds below the content for a window with a border but no title bar
    /// (ResizeClient leaves an empty strip at the bottom). Learned once from the real layout.
    /// </summary>
    private int _gapPx;

    private void TrimBottomGap()
    {
        if (!IsOpen || Root.XamlRoot == null || Root.ActualHeight <= 0) return;
        double scale = Root.XamlRoot.RasterizationScale;
        int gap = (int)Math.Floor((Root.XamlRoot.Size.Height - Root.ActualHeight) * scale);
        if (gap < 2) return;
        _gapPx += gap;
        Services.CrashLog.Trace($"flyout: trimmed {gap}px bottom gap");
        ResizeToContent(Root.ActualHeight);
    }

    private void StartMeters()
    {
        if (_meterTimer == null)
        {
            _meterTimer = DispatcherQueue.CreateTimer();
            _meterTimer.Interval = TimeSpan.FromMilliseconds(40);
            _meterTimer.Tick += (_, _) =>
            {
                try
                {
                    ViewModel.UpdateMeters();
                }
                catch (Exception ex)
                {
                    // Don't log 25 errors a second: stop the meters and record it once.
                    _meterTimer?.Stop();
                    Services.CrashLog.Write("level meters", ex);
                }
            };
        }
        _meterTimer.Start();
    }

    /// <summary>Mouse wheel over any slider: 2% per notch.</summary>
    private void Slider_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider) return;
        int delta = e.GetCurrentPoint(slider).Properties.MouseWheelDelta;
        slider.Value = Math.Clamp(slider.Value + Math.Sign(delta) * 2, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    public void HideFlyout()
    {
        Services.CrashLog.Trace("flyout: hide");
        _meterTimer?.Stop();
        AppWindow.Hide();
        _hiddenAt = DateTime.UtcNow;
        Services.MemoryTrimmer.TrimSoon();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        _meterTimer?.Stop();
        Close();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) return;
        Services.CrashLog.Trace("flyout: deactivated");

        // Don't hide inside the activation change itself: the acrylic backdrop is still switching
        // to its inactive look at that moment, and hiding the window mid-switch can crash WinUI.
        // Hide right after, once Windows has finished moving focus to the other window.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            Services.Guard.Run("hide flyout", () =>
            {
                if (IsOpen && !_allowClose) HideFlyout();
            }));
    }

    // ---------- Event handlers ----------

    private void ProfileChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) ViewModel.SelectProfile(id);
    }

    /// <summary>"+N" button: dark dropdown with the profiles that don't fit as chips.</summary>
    private void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = (Style)Application.Current.Resources["GlassMenuPresenter"],
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        };
        foreach (var chip in ViewModel.OverflowChips)
        {
            var id = chip.Id;
            var item = new MenuFlyoutItem
            {
                Text = chip.Name,
                Icon = new FontIcon { Glyph = "\uE91F", Foreground = chip.CircleBrush },
                Padding = new Thickness(10, 8, 12, 8),
                CornerRadius = new CornerRadius(6),
            };
            if (chip.Hotkey.Length > 0) item.KeyboardAcceleratorTextOverride = chip.Hotkey;
            item.Click += (_, _) => Services.Guard.Run("overflow profile", () => ViewModel.SelectProfile(id));
            menu.Items.Add(item);
        }
        // The "+" button is hidden while this dropdown is shown (no room), so "New profile" lives here.
        menu.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem
        {
            Text = Localization.S.NewProfileFromCurrent,
            Icon = new FontIcon { Glyph = "\uE710" },
            Padding = new Thickness(10, 8, 12, 8),
            CornerRadius = new CornerRadius(6),
        };
        create.Click += (_, _) => Services.Guard.Run("new profile", () => NewProfile_Click(create, new RoutedEventArgs()));
        menu.Items.Add(create);
        menu.ShowAt(OverflowButton);
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var id = ViewModel.NewProfileFromCurrent();
        ViewModel.SelectProfile(id);
    }

    /// <summary>The settings button was clicked; App opens the profile editor.</summary>
    public event Action? EditRequested;

    private void EditProfiles_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        EditRequested?.Invoke();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => ViewModel.SaveIntoActive();

    private void Output_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectOutput(((ComboBox)sender).SelectedIndex);

    private void Input_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectInput(((ComboBox)sender).SelectedIndex);

    private void OutputMute_Click(object sender, RoutedEventArgs e) => ViewModel.OutputMuted = !ViewModel.OutputMuted;

    private void InputMute_Click(object sender, RoutedEventArgs e) => ViewModel.InputMuted = !ViewModel.InputMuted;

    private void AppMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppItem app }) app.Muted = !app.Muted;
    }

    // ---------- Hover ----------

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RowHover =
        new(Microsoft.UI.ColorHelper.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RowIdle = new(Microsoft.UI.Colors.Transparent);

    private void Chip_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileChip chip }) chip.IsHovered = true;
    }

    private void Chip_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileChip chip }) chip.IsHovered = false;
    }

    private void AppRow_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid row) row.Background = RowHover;
    }

    private void AppRow_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid row) row.Background = RowIdle;
    }

    /// <summary>Tints each app's slider with the color taken from its icon.</summary>
    private void AppSlider_Loading(FrameworkElement sender, object args)
    {
        if (sender is not Slider { DataContext: AppItem app } slider) return;
        foreach (var key in AccentKeys)
        {
            slider.Resources[key] = app.Accent;
        }
    }
}
