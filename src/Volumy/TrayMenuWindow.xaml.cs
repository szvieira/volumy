using Volumy.Interop;
using Volumy.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace Volumy;

/// <summary>One row of the tray menu.</summary>
public sealed record TrayMenuItem(
    string Glyph,
    string Text,
    Action? Action,
    string? Shortcut = null,
    Brush? GlyphBrush = null,
    bool IsChecked = false,
    bool IsEnabled = true)
{
    public static readonly TrayMenuItem Separator = new("", "", null);
    public bool IsSeparator => ReferenceEquals(this, Separator);
}

/// <summary>
/// Dark glass tray menu (Raycast-like), opened on right-click of the tray icon.
/// Closes on: picking an item, Esc, clicking anywhere else, or losing focus.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    private const int WidthDip = 290;
    private const int MarginDip = 8;

    private static readonly FontFamily SymbolFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly SolidColorBrush Dim = new(ColorHelper.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    private readonly IntPtr _hwnd;
    private readonly DesktopAcrylicController _acrylic;
    private readonly SystemBackdropConfiguration _backdropConfig;
    private bool _closing;
    private long _shownAt;

    // Right after opening, the taskbar finishes handling the click and takes focus back.
    // That deactivation isn't the user leaving, so ignore it for this long.
    private const int OpenGraceMs = 600;

    public TrayMenuWindow()
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
        bool watching = false;
        Root.Loaded += (_, _) =>
        {
            if (watching || Root.XamlRoot == null) return;
            watching = true;
            Root.XamlRoot.Changed += (_, _) => Guard.Run("menu: trim gap", TrimBottomGap);
        };

        // Keep the glass on even if Windows doesn't give the menu focus.
        _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        _acrylic = new DesktopAcrylicController
        {
            TintColor = ColorHelper.FromArgb(0xFF, 0x1C, 0x1C, 0x21),
            TintOpacity = 0.6f,
            LuminosityOpacity = 0.9f,
            FallbackColor = ColorHelper.FromArgb(0xFF, 0x1C, 0x1C, 0x21),
        };
        _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylic.SetSystemBackdropConfiguration(_backdropConfig);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) return;
            if (Environment.TickCount64 - _shownAt < OpenGraceMs)
            {
                CrashLog.Trace("menu: deactivated during open (ignored)");
                return; // clicks outside still close it (the mouse hook), and so does Esc
            }
            CrashLog.Trace("menu: deactivated");
            // Same as the flyout: never hide inside the activation change itself.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => Guard.Run("menu: deactivated", HideMenu));
        };
        AppWindow.Closing += (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true;
            HideMenu();
        };
    }

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>Screen rectangle of the menu, in physical pixels (for "clicked outside" checks).</summary>
    public RectInt32 Bounds => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    public void ShowAt(IReadOnlyList<TrayMenuItem> items, int cursorX, int cursorY)
    {
        Build(items);

        double scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        int margin = (int)(MarginDip * scale);
        int width = (int)(WidthDip * scale);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursorX, cursorY), DisplayAreaFallback.Primary).WorkArea;

        _scale = scale;
        _margin = margin;
        _area = area;
        _cursorX = cursorX;
        _cursorY = cursorY;

        // Measure the content directly (works before the window is visible), then size and place it.
        // Root_SizeChanged corrects it again once the real layout has run.
        // Start tall so the content is never squeezed (a squeezed menu would report the squeezed height).
        AppWindow.ResizeClient(new SizeInt32(width, area.Height - 2 * margin));
        Root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        PlaceWindow(Root.DesiredSize.Height);

        _shownAt = Environment.TickCount64;
        AppWindow.Show();
        Activate();
        Win32.SetForegroundWindow(_hwnd);
        CrashLog.Trace("menu: shown");
    }

    private double _scale = 1;
    private int _margin;
    private RectInt32 _area;
    private int _cursorX, _cursorY;

    private void PlaceWindow(double contentHeightDip)
    {
        if (contentHeightDip <= 0) return;
        int width = (int)(WidthDip * _scale);
        int maxHeight = _area.Height - 2 * _margin;
        int height = Math.Min((int)Math.Ceiling(contentHeightDip * _scale) + 2 - _gapPx, maxHeight);
        CrashLog.Trace($"menu: size content={contentHeightDip:0} dip, window={height} px, rows={Items.Children.Count}");
        AppWindow.ResizeClient(new SizeInt32(width, height));

        // Open above-left of the cursor (the tray is at the bottom), kept inside the screen.
        var outer = AppWindow.Size;
        int x = Math.Clamp(_cursorX - outer.Width + _margin, _area.X + _margin, _area.X + _area.Width - outer.Width - _margin);
        int y = Math.Clamp(_cursorY - outer.Height - _margin, _area.Y + _margin, _area.Y + _area.Height - outer.Height - _margin);
        AppWindow.Move(new PointInt32(x, y));
    }

    /// <summary>Extra pixels Windows adds below the content (border, no title bar). Learned from the real layout.</summary>
    private int _gapPx;

    private void TrimBottomGap()
    {
        if (!IsOpen || Root.XamlRoot == null || Root.ActualHeight <= 0) return;
        double scale = Root.XamlRoot.RasterizationScale;
        int gap = (int)Math.Floor((Root.XamlRoot.Size.Height - Root.ActualHeight) * scale);
        if (gap < 2) return;
        _gapPx += gap;
        CrashLog.Trace($"menu: trimmed {gap}px bottom gap");
        PlaceWindow(Root.ActualHeight);
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsOpen) Guard.Run("menu: resize", () => PlaceWindow(e.NewSize.Height));
    }

    public void HideMenu()
    {
        if (!IsOpen) return;
        CrashLog.Trace("menu: hide");
        AppWindow.Hide();
        MemoryTrimmer.TrimSoon();
    }

    public void CloseForExit()
    {
        _closing = true;
        Guard.Run("menu backdrop", _acrylic.Dispose);
        Close();
    }

    private void Build(IReadOnlyList<TrayMenuItem> items)
    {
        Items.Children.Clear();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                Items.Children.Add(new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(8, 5, 8, 5),
                });
                continue;
            }

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (item.Glyph.Length > 0)
            {
                row.Children.Add(new FontIcon
                {
                    Glyph = item.Glyph,
                    FontFamily = SymbolFont,
                    FontSize = 15,
                    Foreground = item.GlyphBrush ?? new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var text = new TextBlock
            {
                Text = item.Text,
                FontSize = 14,
                FontWeight = item.IsChecked ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                right.Children.Add(new TextBlock { Text = item.Shortcut, FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            }
            if (item.IsChecked)
            {
                right.Children.Add(new FontIcon { Glyph = "", FontFamily = SymbolFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            }
            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            if (item.Action == null || !item.IsEnabled)
            {
                // Info line (e.g. version): not clickable.
                row.Opacity = 0.55;
                row.Margin = new Thickness(10, 7, 12, 7);
                Items.Children.Add(row);
                continue;
            }

            var button = new HandButton
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 12, 8),
                MinHeight = 36,
            };
            var action = item.Action;
            button.Click += (_, _) =>
            {
                HideMenu();
                // Run after the menu is gone, so actions that open windows get focus.
                DispatcherQueue.TryEnqueue(() => Guard.Run($"menu: {item.Text}", action));
            };
            Items.Children.Add(button);
        }
    }

    private void Root_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
    }
}
