using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using EmojiAnywhere.Core;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// What the flyout needs from the tray host
public interface ITrayActions
{
    Settings Settings { get; }
    bool Paused { get; }
    string LastApp { get; }
    void SetEnabled(bool on);
    void TogglePause();
    void SetStartup(bool on);
    void OpenSource();
    void Quit();
}

// The small panel that opens from the tray icon. Every setting lives here.
public partial class TrayFlyout : Window
{
    private readonly ITrayActions _host;
    private readonly int _anchorX;   // where the tray icon was clicked, in screen pixels
    private bool _closing;

    public TrayFlyout(ITrayActions host, int anchorX)
    {
        InitializeComponent();
        _host = host;
        _anchorX = anchorX;
        ApplyTheme();
        AppIcon.Source = LoadIcon();

        EnabledSwitch.IsChecked = host.Settings.Enabled;
        // Checked/Unchecked (not Click) so the switches also work from the keyboard and screen readers
        RoutedEventHandler onEnabled = (_, _) => { AnimateSwitch(EnabledSwitch); _host.SetEnabled(EnabledSwitch.IsChecked == true); Refresh(); };
        EnabledSwitch.Checked += onEnabled;
        EnabledSwitch.Unchecked += onEnabled;
        PauseButton.Click += (_, _) => { _host.TogglePause(); Refresh(); };
        StartupSwitch.IsChecked = host.Settings.StartWithWindows;
        if (AppPaths.IsPackaged) { StartupRow.Visibility = Visibility.Collapsed; StartupStoreNote.Visibility = Visibility.Visible; }
        RoutedEventHandler onStartup = (_, _) => { AnimateSwitch(StartupSwitch); _host.SetStartup(StartupSwitch.IsChecked == true); };
        StartupSwitch.Checked += onStartup;
        StartupSwitch.Unchecked += onStartup;
        QuietSwitch.IsChecked = host.Settings.QuietInShortcodeApps;
        RoutedEventHandler onQuiet = (_, _) => { AnimateSwitch(QuietSwitch); _host.Settings.QuietInShortcodeApps = QuietSwitch.IsChecked == true; _host.Settings.Save(); };
        QuietSwitch.Checked += onQuiet;
        QuietSwitch.Unchecked += onQuiet;
        SourceButton.Click += (_, _) => { _host.OpenSource(); Close(); };
        QuitButton.Click += (_, _) => { Close(); _host.Quit(); };

        BuildChoices(ResultsRow, "results", new[] { ("4", 4.0), ("5", 5.0), ("6", 6.0), ("8", 8.0), ("10", 10.0) },
            host.Settings.MaxResults, v => { _host.Settings.MaxResults = (int)v; _host.Settings.Save(); });
        BuildChoices(SizeRow, "size", new[] { ("S", 0.9), ("M", 1.0), ("L", 1.2), ("XL", 1.4) },
            host.Settings.PopupScale, v => { _host.Settings.PopupScale = v; _host.Settings.Save(); });

        BuildChips(IgnoredChips, () => _host.Settings.IgnoredApps, l => _host.Settings.IgnoredApps = l, "Ignore {0}");
        BuildChips(PasteChips, () => _host.Settings.PasteModeApps, l => _host.Settings.PasteModeApps = l, "Use in {0}");
        Refresh();

        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => _closing = true;
        Loaded += (_, _) => { PlaceAboveTaskbar(); PlayOpen(); EnabledSwitch.Focus(); };
    }

    public bool IsClosing => _closing;

    private void Refresh()
    {
        bool on = _host.Settings.Enabled;
        Status.Text = !on ? "Off" : _host.Paused ? "Paused for 1 hour" : "On. Type : and two letters anywhere";
        PauseText.Text = _host.Paused ? "Resume now" : "Pause for 1 hour";
        PauseButton.IsEnabled = on;
    }

    // A row of choices like 4 5 6 8 10. Picking one saves right away.
    private static void BuildChoices(UniformGrid row, string group, (string label, double value)[] options, double current, Action<double> pick)
    {
        foreach (var (label, value) in options)
        {
            var rb = new RadioButton
            {
                Content = label, GroupName = group, Style = (Style)row.FindResource("Segment"),
                IsChecked = Math.Abs(current - value) < 0.05
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, $"{group} {label}");
            rb.Checked += (_, _) => pick(value);
            row.Children.Add(rb);
        }
    }

    // App chips: click one to remove it. The last app you typed in can be added with one click.
    private void BuildChips(WrapPanel panel, Func<List<string>> get, Action<List<string>> set, string addFormat)
    {
        panel.Children.Clear();
        foreach (var app in get())
        {
            var chip = Chip(app, "", $"Remove {app}");
            chip.Click += (_, _) => FadeOut(chip, () =>
            {
                set(get().Where(a => !a.Equals(app, StringComparison.OrdinalIgnoreCase)).ToList());
                _host.Settings.Save();
                BuildChips(panel, get, set, addFormat);
            });
            panel.Children.Add(chip);
        }

        string last = _host.LastApp;
        if (last.Length > 0 && !get().Contains(last, StringComparer.OrdinalIgnoreCase))
        {
            var add = Chip(string.Format(addFormat, last), "", string.Format(addFormat, last));
            add.Click += (_, _) =>
            {
                set(get().Append(last).ToList());
                _host.Settings.Save();
                BuildChips(panel, get, set, addFormat);
            };
            panel.Children.Add(add);
        }
        else if (get().Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "None yet. Type in an app, then open this to add it.",
                Foreground = (Brush)FindResource("Fly.Muted"), FontSize = 12, Margin = new Thickness(2, 0, 0, 8)
            });
        }
    }

    private Button Chip(string text, string glyph, string name)
    {
        var b = new Button
        {
            Style = (Style)FindResource("Chip"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Fly.Icons"), FontSize = 10, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    // ---------- Motion. Skipped when Windows animations are turned off. ----------
    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private void PlayOpen()
    {
        if (!Animate) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    // The thumb has already jumped to its new spot. Start it from the old spot and slide it over.
    private static void AnimateSwitch(ToggleButton sw)
    {
        if (!Animate || sw.Template.FindName("Move", sw) is not TranslateTransform move || sw.Template.FindName("Fill", sw) is not UIElement fill) return;
        bool on = sw.IsChecked == true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? -18 : 18, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        fill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0 : 1, on ? 1 : 0, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
    }

    private static void FadeOut(UIElement el, Action done)
    {
        if (!Animate) { done(); return; }
        var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        a.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, a);
    }

    // ---------- Placement and theme ----------
    private void PlaceAboveTaskbar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        // Bottom right, right above the tray, like the Windows volume and Wi-Fi panels.
        // If the taskbar sits on the left or top instead, hug that side.
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(_anchorX, System.Windows.Forms.Cursor.Position.Y));
        var area = screen.WorkingArea;
        bool taskbarLeft = area.Left > screen.Bounds.Left, taskbarTop = area.Top > screen.Bounds.Top;
        int x = taskbarLeft ? area.Left : area.Right - w;
        int y = taskbarTop ? area.Top : area.Bottom - h;
        SetWindowPos(hwnd, (IntPtr)(-1), x, y, 0, 0, 0x0001); // keep size, topmost
    }

    // Used by the preview renderer to draw both themes
    public void UseTheme(bool light) => ApplyTheme(light);

    private void ApplyTheme(bool? force = null)
    {
        bool light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (key?.GetValue("SystemUsesLightTheme") as int? ?? 1) != 0; // tray flyouts follow the Windows theme
        }
        catch { }
        if (force is bool f) light = f;

        void Set(string name, uint argb) => Resources[name] = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        if (light)
        {
            Set("Fly.Background", 0xFFF3F4F7); Set("Fly.Card", 0xFFFFFFFF); Set("Fly.Border", 0x1A000000);
            Set("Fly.Text", 0xFF1B1D22); Set("Fly.Muted", 0xFF5E6470); Set("Fly.Hover", 0x0D000000);
            Set("Fly.Track", 0xFFC9CDD5); Set("Fly.Accent", 0xFFC92A62); Set("Fly.OnAccent", 0xFFFFFFFF);
        }
        else
        {
            Set("Fly.Background", 0xFF202227); Set("Fly.Card", 0xFF2A2C32); Set("Fly.Border", 0x1FFFFFFF);
            Set("Fly.Text", 0xFFF1F2F5); Set("Fly.Muted", 0xFFA4A9B4); Set("Fly.Hover", 0x14FFFFFF);
            Set("Fly.Track", 0xFF4A4D55); Set("Fly.Accent", 0xFFFF6A92); Set("Fly.OnAccent", 0xFF1B1D22);
        }
    }

    private static ImageSource? LoadIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(asm.GetManifestResourceNames().First(n => n.EndsWith("app.ico")))!;
            var decoder = new IconBitmapDecoder(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).First(f => f.PixelWidth <= 128);
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
