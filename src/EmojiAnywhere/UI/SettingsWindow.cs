using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EmojiAnywhere.Core;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// Simple settings window. Changes apply right away when you press Save.
public sealed class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly CheckBox _startup = new() { Content = "Start with Windows", Margin = new Thickness(0, 0, 0, 16) };
    private readonly Slider _results = new() { Minimum = 3, Maximum = 10, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Slider _size = new() { Minimum = 0.8, Maximum = 1.6, IsSnapToTickEnabled = true, TickFrequency = 0.1, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _ignored = MultiLine();
    private readonly TextBox _paste = MultiLine();

    public SettingsWindow(Settings settings)
    {
        _settings = settings;
        Title = "Emoji Anywhere settings";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;

        _startup.IsChecked = settings.StartWithWindows;
        _results.Value = settings.MaxResults;
        _size.Value = settings.PopupScale;
        _ignored.Text = string.Join(Environment.NewLine, settings.IgnoredApps);
        _paste.Text = string.Join(Environment.NewLine, settings.PasteModeApps);

        var resultsLabel = new TextBlock();
        var sizeLabel = new TextBlock();
        void UpdateLabels()
        {
            resultsLabel.Text = $"Results shown: {(int)_results.Value}";
            sizeLabel.Text = $"Popup size: {_size.Value * 100:0}%";
        }
        _results.ValueChanged += (_, _) => UpdateLabels();
        _size.ValueChanged += (_, _) => UpdateLabels();
        UpdateLabels();

        var save = new Button { Content = "Save", Width = 90, Padding = new Thickness(0, 4, 0, 4), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Padding = new Thickness(0, 4, 0, 4), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => { Save(); Close(); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                _startup,
                resultsLabel, _results, Gap(),
                sizeLabel, _size, Gap(),
                Label("Ignored apps (one per line, name without .exe)"), _ignored, Gap(),
                Label("Apps that need paste mode (if emojis don't appear in an app, add it here)"), _paste, Gap(),
                Hint("Emoji Anywhere never connects to the internet and never saves what you type."),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { save, cancel } }
            }
        };
    }

    private void Save()
    {
        _settings.StartWithWindows = _startup.IsChecked == true;
        _settings.MaxResults = (int)_results.Value;
        _settings.PopupScale = Math.Round(_size.Value, 1);
        _settings.IgnoredApps = Lines(_ignored.Text);
        _settings.PasteModeApps = Lines(_paste.Text);
        _settings.Save();
        ApplyStartup(_settings.StartWithWindows);
    }

    public static void ApplyStartup(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (on) key?.SetValue("EmojiAnywhere", $"\"{Environment.ProcessPath}\"");
            else key?.DeleteValue("EmojiAnywhere", false);
        }
        catch { }
    }

    private static List<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static TextBox MultiLine() => new()
    {
        AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4)
    };

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
    private static TextBlock Hint(string text) => new() { Text = text, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
    private static FrameworkElement Gap() => new Border { Height = 14 };
}
