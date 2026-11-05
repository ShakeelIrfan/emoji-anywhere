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
    private readonly ToolStripMenuItem _toggle, _pause;
    private SettingsWindow? _settingsWindow;
    private readonly System.Windows.Threading.DispatcherTimer _pauseTimer = new() { Interval = TimeSpan.FromHours(1) };

    public TrayHost(Settings settings, AutocompleteController controller, Action quit)
    {
        _settings = settings;
        _controller = controller;

        _toggle = new ToolStripMenuItem("Enabled", null, (_, _) => SetEnabled(!_settings.Enabled));
        _pause = new ToolStripMenuItem("Pause for 1 hour", null, (_, _) => TogglePause());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggle);
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
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

    public void ShowWelcome() =>
        _icon.ShowBalloonTip(6000, "Emoji Anywhere is running",
            "Type : and a few letters in any app, like :fire. It starts with Windows. Right click the tray icon for settings.",
            ToolTipIcon.None);

    private void OpenSettings()
    {
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Refresh()
    {
        _toggle.Checked = _settings.Enabled;
        _pause.Checked = _controller.Paused;
        _pause.Enabled = _settings.Enabled;
        string state = !_settings.Enabled ? "off" : _controller.Paused ? "paused" : "on";
        _icon.Text = $"Emoji Anywhere ({state})";
    }

    // The app icon, picked at the tray's size so it stays sharp at any DPI
    private static Icon MakeIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        string name = asm.GetManifestResourceNames().First(n => n.EndsWith("app.ico"));
        using var stream = asm.GetManifestResourceStream(name)!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
