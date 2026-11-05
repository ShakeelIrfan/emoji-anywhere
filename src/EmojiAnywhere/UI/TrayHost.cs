using System.Diagnostics;
using System.Drawing;
using EmojiAnywhere.Core;
using EmojiAnywhere.Input;
using Forms = System.Windows.Forms;

namespace EmojiAnywhere.UI;

// The tray icon. Clicking it opens the settings panel (TrayFlyout).
public sealed class TrayHost : IDisposable, ITrayActions
{
    private readonly Forms.NotifyIcon _icon;
    private readonly AutocompleteController _controller;
    private readonly Action _quit;
    private readonly System.Windows.Threading.DispatcherTimer _pauseTimer = new() { Interval = TimeSpan.FromHours(1) };
    private TrayFlyout? _flyout;
    private DateTime _flyoutClosedAt;

    public Settings Settings { get; }
    public bool Paused => _controller.Paused;

    public string LastApp
    {
        get
        {
            string name = ContextGuard.ProcessName(_controller.LastTypedWindow);
            return name.Equals("EmojiAnywhere", StringComparison.OrdinalIgnoreCase) || name.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "" : name;
        }
    }

    public TrayHost(Settings settings, AutocompleteController controller, Action quit)
    {
        Settings = settings;
        _controller = controller;
        _quit = quit;

        _icon = new Forms.NotifyIcon { Icon = MakeIcon(), Visible = true };
        _icon.MouseUp += (_, e) => { if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right) ToggleFlyout(); };

        _pauseTimer.Tick += (_, _) => TogglePause();
        Refresh();
    }

    private void ToggleFlyout()
    {
        // Clicking the icon while the panel is open closes it (the click itself already closed it)
        if (_flyout != null || (DateTime.UtcNow - _flyoutClosedAt).TotalMilliseconds < 300) return;
        _flyout = new TrayFlyout(this, Forms.Cursor.Position.X);
        _flyout.Closed += (_, _) => { _flyout = null; _flyoutClosedAt = DateTime.UtcNow; };
        _flyout.Show();
        _flyout.Activate();
    }

    public void SetEnabled(bool on)
    {
        Settings.Enabled = on;
        Settings.Save();
        if (!on) _controller.Reset();
        Refresh();
    }

    public void TogglePause()
    {
        _controller.Paused = !_controller.Paused;
        if (_controller.Paused) { _controller.Reset(); _pauseTimer.Start(); } else _pauseTimer.Stop();
        Refresh();
    }

    public void SetStartup(bool on)
    {
        Settings.StartWithWindows = on;
        Settings.Save();
        Settings.ApplyStartup(on);
    }

    public void OpenSource()
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/ShakeelIrfan/emoji-anywhere") { UseShellExecute = true }); } catch { }
    }

    public void Quit() => _quit();

    public void ShowWelcome() =>
        _icon.ShowBalloonTip(6000, "Emoji Anywhere is running",
            "Type : and a few letters in any app, like :fire. Click the tray icon for settings.",
            Forms.ToolTipIcon.None);

    private void Refresh()
    {
        string state = !Settings.Enabled ? "off" : _controller.Paused ? "paused" : "on";
        _icon.Text = $"Emoji Anywhere ({state})";
    }

    // The app icon, picked at the tray's size so it stays sharp at any DPI
    private static Icon MakeIcon()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        string name = asm.GetManifestResourceNames().First(n => n.EndsWith("app.ico"));
        using var stream = asm.GetManifestResourceStream(name)!;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _flyout?.Close();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
