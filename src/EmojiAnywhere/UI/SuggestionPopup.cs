using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EmojiAnywhere.Core;
using EmojiAnywhere.Input;
using EmojiAnywhere.Native;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// The suggestion list. It never takes focus, so the app you type in keeps the cursor.
public sealed class SuggestionPopup : Window
{
    private readonly StackPanel _list = new();
    private readonly Border _card;
    private int _selected;
    private IntPtr _hwnd;

    public event Action<int>? ItemClicked;
    public IntPtr Hwnd => _hwnd; // safe to read from any thread
    public bool IsOpen => IsVisible;

    public SuggestionPopup()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;

        _card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Margin = new Thickness(12), // room for the shadow
            Child = _list,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.25, Direction = 270 }
        };
        Content = _card;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST);
        };
    }

    public int Selected
    {
        get => _selected;
        set { _selected = value; Highlight(); }
    }

    public void Show(List<EmojiEntry> items, CaretLocator.Spot at, double scale)
    {
        Fill(items, scale);
        if (!IsVisible) base.Show();
        UpdateLayout();
        Place(at);
    }

    // Builds the rows without showing the window. Also used to render previews for the README.
    public void Fill(List<EmojiEntry> items, double scale, bool? light = null)
    {
        ApplyTheme(light);
        _list.Children.Clear();
        for (int i = 0; i < items.Count; i++) _list.Children.Add(Row(items[i], i, scale));
        _selected = 0;
        Highlight();
    }

    public new void Hide()
    {
        if (!IsVisible) return;
        base.Hide();
        // Wait for a real pause, so the cleanup never lands while someone is still typing
        MemoryTrim.Schedule(TimeSpan.FromSeconds(30));
    }

    private UIElement Row(EmojiEntry e, int index, double scale)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8 * scale, 4 * scale, 12 * scale, 4 * scale),
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    EmojiIcon(e.Unicode, scale),
                    new TextBlock { Text = ":" + e.PrimaryShortcode + ":", FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 13 * scale, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        row.MouseLeftButtonDown += (_, _) => ItemClicked?.Invoke(index);
        return row;
    }

    // Color emoji drawn by Windows. Falls back to plain text if drawing ever fails.
    private static FrameworkElement EmojiIcon(string emoji, double scale)
    {
        double size = 22 * scale;
        var source = EmojiRenderer.Get(emoji);
        if (source == null)
            return new TextBlock { Text = emoji, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 18 * scale, Width = 28 * scale, VerticalAlignment = VerticalAlignment.Center };

        var img = new Image { Source = source, Width = size, Height = size, Margin = new Thickness(0, 0, 6 * scale, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    private void Highlight()
    {
        for (int i = 0; i < _list.Children.Count; i++)
            ((Border)_list.Children[i]).Background = i == _selected ? _accentBg : Brushes.Transparent;
    }

    private Brush _accentBg = Brushes.LightBlue;

    private void ApplyTheme(bool? forceLight = null)
    {
        bool light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { }
        if (forceLight is bool f) light = f;

        _card.Background = new SolidColorBrush(light ? Color.FromRgb(0xF9, 0xF9, 0xF9) : Color.FromRgb(0x2B, 0x2B, 0x2B));
        _card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        _accentBg = new SolidColorBrush(light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        TextElement.SetForeground(_card, new SolidColorBrush(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White));
    }

    // Positions in physical pixels so it lands right on any monitor and DPI
    private void Place(CaretLocator.Spot at)
    {
        if (_hwnd == IntPtr.Zero) return;
        GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = at.X - 12, y = at.Y - 6;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(at.X, at.Y)).WorkingArea;
        if (x + w > screen.Right) x = screen.Right - w;
        if (y + h > screen.Bottom) y = at.Y - h - 20; // flip above the caret near the bottom edge
        if (x < screen.Left) x = screen.Left;

        SetWindowPos(_hwnd, (IntPtr)(-1), x, y, 0, 0, 0x0001 | 0x0010); // NOSIZE | NOACTIVATE
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Win32.RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
