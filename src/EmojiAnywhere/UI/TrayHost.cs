using System.Drawing;
using System.Windows.Forms;
using EmojiAnywhere.Core;
using EmojiAnywhere.Input;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// The tray icon and its menu.
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Settings _settings;
    private readonly AutocompleteController _controller;
    private readonly ToolStripMenuItem _toggle, _pause, _startup;
    private readonly System.Windows.Threading.DispatcherTimer _pauseTimer = new() { Interval = TimeSpan.FromHours(1) };

    public TrayHost(Settings settings, AutocompleteController controller, Action quit)
    {
        _settings = settings;
        _controller = controller;

        _toggle = new ToolStripMenuItem("Enabled", null, (_, _) => SetEnabled(!_settings.Enabled));
        _pause = new ToolStripMenuItem("Pause for 1 hour", null, (_, _) => TogglePause());
        _startup = new ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartup(!_settings.StartWithWindows));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggle);
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startup);
        menu.Items.Add("Open settings file", null, (_, _) => OpenSettingsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new NotifyIcon { Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => SetEnabled(!_settings.Enabled);

        _pauseTimer.Tick += (_, _) => TogglePause();
        Refresh();
    }

    private void SetEnabled(bool on)
    {
        _settings.Enabled = on;
        _settings.Save();
        if (!on) _controller.Reset();
        Refresh();
    }

    private void TogglePause()
    {
        _controller.Paused = !_controller.Paused;
        if (_controller.Paused) { _controller.Reset(); _pauseTimer.Start(); } else _pauseTimer.Stop();
        Refresh();
    }

    private void SetStartup(bool on)
    {
        _settings.StartWithWindows = on;
        _settings.Save();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (on) key?.SetValue("EmojiAnywhere", $"\"{Environment.ProcessPath}\"");
        else key?.DeleteValue("EmojiAnywhere", false);
        Refresh();
    }

    private static void OpenSettingsFolder() =>
        System.Diagnostics.Process.Start("explorer.exe", AppPaths.Folder);

    private void Refresh()
    {
        _toggle.Checked = _settings.Enabled;
        _pause.Checked = _controller.Paused;
        _pause.Enabled = _settings.Enabled;
        _startup.Checked = _settings.StartWithWindows;
        string state = !_settings.Enabled ? "off" : _controller.Paused ? "paused" : "on";
        _icon.Text = $"Emoji Anywhere ({state})";
    }

    // Draws the tray icon at runtime, so no image file is needed
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI Emoji", 18, GraphicsUnit.Pixel);
            g.DrawString("😀", font, Brushes.Gold, -1, 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
