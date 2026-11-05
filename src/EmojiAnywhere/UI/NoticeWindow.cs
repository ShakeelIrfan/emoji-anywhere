using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// A small, friendly message window for the few times something needs the person's attention:
// already running, keyboard access blocked, or an unexpected error. Follows the Windows theme.
public sealed class NoticeWindow : Window
{
    public string? Choice { get; private set; }

    private NoticeWindow(string glyph, string title, string message, string primary, string? secondary, bool reportLink)
    {
        Title = "Emoji Anywhere";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;

        bool light = IsLightTheme();
        Brush bg = Solid(light ? 0xFFF3F4F7 : 0xFF202227), text = Solid(light ? 0xFF1B1D22 : 0xFFF1F2F5);
        Brush muted = Solid(light ? 0xFF5E6470 : 0xFFA4A9B4), border = Solid(light ? 0x1A000000u : 0x1FFFFFFFu);
        Brush btnBg = Solid(light ? 0xFF1B1D22 : 0xFFF1F2F5), btnText = Solid(light ? 0xFFFFFFFF : 0xFF1B1D22);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (secondary != null) buttons.Children.Add(MakeButton(secondary, Brushes.Transparent, text, border, () => Finish(secondary)));
        var main = MakeButton(primary, btnBg, btnText, btnBg, () => Finish(primary));
        main.IsDefault = true;
        buttons.Children.Add(main);

        var body = new StackPanel { Width = 360 };
        // Color emoji via the app's Direct2D drawer (plain WPF text would be black and white)
        if (EmojiRenderer.Get(glyph) is ImageSource pic)
        {
            var img = new Image { Source = pic, Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            body.Children.Add(img);
        }
        else body.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 34, Margin = new Thickness(0, 0, 0, 10) });
        body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = text, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = message, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 20 });
        if (reportLink)
        {
            var link = new TextBlock { Margin = new Thickness(0, 12, 0, 0) };
            var hl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Report a problem on GitHub")) { Foreground = text };
            hl.Click += (_, _) => Open("https://github.com/ShakeelIrfan/emoji-anywhere/issues/new");
            link.Inlines.Add(hl);
            body.Children.Add(link);
        }
        body.Children.Add(buttons);

        Content = new Border
        {
            Margin = new Thickness(16), Padding = new Thickness(24), CornerRadius = new CornerRadius(12),
            Background = bg, BorderBrush = border, BorderThickness = new Thickness(1), Child = body,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.25 }
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Finish(secondary ?? primary); };
    }

    /// Shows the notice and returns the label of the button that was clicked.
    public static string Show(string glyph, string title, string message, string primary, string? secondary = null, bool reportLink = false)
    {
        var w = new NoticeWindow(glyph, title, message, primary, secondary, reportLink);
        w.ShowDialog();
        return w.Choice ?? secondary ?? primary;
    }

    private void Finish(string choice) { Choice = choice; Close(); }

    private static Button MakeButton(string label, Brush bg, Brush fg, Brush border, Action onClick)
    {
        var template = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
        bd.SetValue(Border.BackgroundProperty, bg);
        bd.SetValue(Border.BorderBrushProperty, border);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bd.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bd.AppendChild(cp);
        template.VisualTree = bd;
        var b = new Button { Content = label, Foreground = fg, Template = template, Margin = new Thickness(8, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.SemiBold };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static SolidColorBrush Solid(uint argb) => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { return true; }
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
