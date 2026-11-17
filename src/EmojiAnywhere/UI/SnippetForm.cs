using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EmojiAnywhere.Core;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// A small form shown before a snippet with {input:...} or {choice:...} fields is inserted.
// Enter inserts, Esc cancels. Returns null when cancelled.
public sealed class SnippetForm : Window
{
    private readonly List<(SnippetField field, Control input)> _inputs = new();
    private Dictionary<string, string>? _result;

    public static Dictionary<string, string>? Ask(string title, List<SnippetField> fields, Window? owner = null, IReadOnlyDictionary<string, string>? defaults = null)
    {
        var f = new SnippetForm(title, fields, defaults);
        if (owner != null) { f.Owner = owner; f.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        f.ShowDialog();
        return f._result;
    }

    private SnippetForm(string title, List<SnippetField> fields, IReadOnlyDictionary<string, string>? defaults)
    {
        bool light = IsLight();
        var bg = new SolidColorBrush(light ? Color.FromRgb(0xF7, 0xF8, 0xFA) : Color.FromRgb(0x20, 0x20, 0x20));
        var card = new SolidColorBrush(light ? Colors.White : Color.FromRgb(0x2B, 0x2B, 0x2B));
        var text = new SolidColorBrush(light ? Color.FromRgb(0x1B, 0x1D, 0x22) : Colors.White);
        var muted = new SolidColorBrush(light ? Color.FromRgb(0x64, 0x6A, 0x76) : Color.FromRgb(0xA8, 0xAD, 0xB7));
        var border = new SolidColorBrush(light ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var accent = new SolidColorBrush(Color.FromRgb(0xD6, 0x33, 0x6C));

        Title = "Fill in: " + title;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 380;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = bg;
        Foreground = text;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), TextTrimming = TextTrimming.CharacterEllipsis });

        foreach (var field in fields)
        {
            stack.Children.Add(new TextBlock { Text = field.Name, Foreground = muted, Margin = new Thickness(0, 0, 0, 4) });
            Control input;
            if (field.Choices is { Length: > 0 } choices)
            {
                var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, Padding = new Thickness(6, 5, 6, 5) };
                input = combo;
            }
            else
            {
                input = new TextBox
                {
                    Padding = new Thickness(6, 5, 6, 5), Background = card, Foreground = text, BorderBrush = border,
                    CaretBrush = text, MaxLength = 10_000,
                    Text = defaults != null && defaults.TryGetValue(field.Name, out var d) ? d : "",
                };
            }
            AutomationPropertiesName(input, field.Name);
            input.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(input);
            _inputs.Add((field, input));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var insert = new Button { Content = "Insert", Padding = new Thickness(14, 6, 14, 6), IsDefault = true, Background = accent, Foreground = Brushes.White, BorderBrush = accent };
        insert.Click += (_, _) => { _result = Values(); Close(); };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);
        buttons.Children.Add(insert);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            ForceForeground(new WindowInteropHelper(this).Handle);
            if (_inputs.Count > 0)
            {
                _inputs[0].input.Focus(); Keyboard.Focus(_inputs[0].input);
                if (_inputs[0].input is TextBox tb) tb.SelectAll();
            }
        };
    }

    private Dictionary<string, string> Values()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, input) in _inputs)
            d[field.Name] = input switch
            {
                TextBox t => t.Text,
                ComboBox c => c.SelectedItem as string ?? "",
                _ => "",
            };
        return d;
    }

    private static void AutomationPropertiesName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);

    private static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { return true; }
    }

    // Windows only lets the app you are using bring a window to the front. Borrowing its input queue for a moment
    // lets the form come up in front of it, so you can type straight away.
    private static void ForceForeground(IntPtr hwnd)
    {
        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        if (attached) AttachThreadInput(me, fgThread, false);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
