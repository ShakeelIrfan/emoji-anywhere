using System.IO;
using System.Windows;
using System.Windows.Media;
using EmojiAnywhere.Core;
using EmojiAnywhere.UI;

// Opens the tray panel off screen and drives every control through accessibility.
class Program
{
    static string Out = Path.Combine(Path.GetTempPath(), "EmojiAnywhereFlyoutTest-" + Guid.NewGuid().ToString("N"));

    [STAThread]
    static int Main()
    {
        Directory.CreateDirectory(Out);
        int code = FlyoutTest();
        try { Directory.Delete(Out, true); } catch { }
        return code;
    }

    class RecordingHost : ITrayActions
    {
        public Settings Settings { get; } = new() { IgnoredApps = new() { "WindowsTerminal", "cmd" }, PasteModeApps = new() };
        public bool Paused { get; set; }
        public string LastApp => "Discord";
        public bool? StartupSetTo; public bool Quitted; public bool SourceOpened;
        public void SetEnabled(bool on) { Settings.Enabled = on; }
        public void TogglePause() { Paused = !Paused; }
        public void SetStartup(bool on) { StartupSetTo = on; }
        public void OpenSource() { SourceOpened = true; }
        public void Quit() { Quitted = true; }
    }

    static void Pump(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); frame.Continue = false; };
        t.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && match(t)) return t;
            if (Find(c, match) is T found) return found;
        }
        return null;
    }

    static void Invoke(System.Windows.Controls.Primitives.ButtonBase b)
    {
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b);
        if (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider inv) inv.Invoke();
        else if (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle) is System.Windows.Automation.Provider.IToggleProvider tog) tog.Toggle();
        else if (peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem) is System.Windows.Automation.Provider.ISelectionItemProvider sel) sel.Select();
        Pump(250);
    }

    static string Name(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

    // Drives the flyout through accessibility, no real keyboard or mouse input
    static int FlyoutTest()
    {
        Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", System.IO.Path.Combine(Out, "flyout-data"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int pass = 0, fail = 0;
        void Check(string n, bool ok) { if (ok) pass++; else fail++; Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {n}"); }

        var host = new RecordingHost();
        var f = new TrayFlyout(host, -30000) { Left = -32000, Top = -32000, ShowActivated = false };
        f.Show();
        Pump(400);

        var sw = Find<System.Windows.Controls.Primitives.ToggleButton>(f, t => Name(t) == "Emoji Anywhere on or off")!;
        Check("on/off switch starts on", sw.IsChecked == true);
        Invoke(sw);
        Check("on/off switch turns it off", host.Settings.Enabled == false);
        Check("pause is disabled while off", !((System.Windows.Controls.Button)f.FindName("PauseButton")).IsEnabled);
        Invoke(sw);
        Check("switch turns it back on", host.Settings.Enabled);

        Invoke(((System.Windows.Controls.Button)f.FindName("PauseButton")));
        Check("pause button pauses", host.Paused);
        Check("button now says resume", ((System.Windows.Controls.TextBlock)f.FindName("PauseText")).Text == "Resume now");
        Invoke(((System.Windows.Controls.Button)f.FindName("PauseButton")));
        Check("resume works", !host.Paused);

        Invoke(Find<System.Windows.Controls.RadioButton>(f, r => Name(r) == "results 8")!);
        Check("results set to 8", host.Settings.MaxResults == 8);
        Invoke(Find<System.Windows.Controls.RadioButton>(f, r => Name(r) == "size L")!);
        Check("popup size set to large", Math.Abs(host.Settings.PopupScale - 1.2) < 0.01);

        var startup = Find<System.Windows.Controls.Primitives.ToggleButton>(f, t => Name(t) == "Start with Windows")!;
        Invoke(startup);
        Check("start with Windows can be turned off", host.StartupSetTo == false);

        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Remove cmd")!);
        Pump(300);
        Check("removing a chip removes the app", !host.Settings.IgnoredApps.Contains("cmd"));
        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Ignore Discord")!);
        Check("adding the last app works", host.Settings.IgnoredApps.Contains("Discord"));
        Check("add chip is gone once added", Find<System.Windows.Controls.Button>(f, b => Name(b) == "Ignore Discord") == null);
        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Use in Discord")!);
        Check("paste mode app added", host.Settings.PasteModeApps.Contains("Discord"));

        var saved = File.ReadAllText(System.IO.Path.Combine(Out, "flyout-data", "settings.json"));
        Check("changes are saved to the settings file", saved.Contains("\"MaxResults\": 8") && saved.Contains("Discord"));

        Invoke(((System.Windows.Controls.Button)f.FindName("QuitButton")));
        Check("quit closes the panel and quits", host.Quitted && f.IsClosing);
        Console.WriteLine($"RESULT pass={pass} fail={fail}");
        return fail == 0 ? 0 : 1;
    }

}
