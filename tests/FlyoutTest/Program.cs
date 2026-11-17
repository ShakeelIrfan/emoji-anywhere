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
        public bool SnippetsOpened;
        public void OpenSnippets() { SnippetsOpened = true; }
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

    // The snippet editor, driven through its controls (no keyboard or mouse)
    static void EditorTests(Action<string, bool> Check)
    {
        // Formatting turns into HTML other apps understand
        var doc = new System.Windows.Documents.FlowDocument();
        var p = new System.Windows.Documents.Paragraph();
        p.Inlines.Add(new System.Windows.Documents.Run("Hi "));
        p.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("you")));
        p.Inlines.Add(new System.Windows.Documents.Run(" & "));
        p.Inlines.Add(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("site")) { NavigateUri = new Uri("https://example.com") });
        p.Inlines.Add(new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("bad")) { NavigateUri = new Uri("javascript:alert(1)") });
        doc.Blocks.Add(p);
        var list = new System.Windows.Documents.List();
        list.ListItems.Add(new System.Windows.Documents.ListItem(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("one"))));
        doc.Blocks.Add(list);
        string html = RichTextConvert.Html(doc);
        if (Environment.GetEnvironmentVariable("SHOW_HTML") != null) Console.WriteLine("HTML: " + html);
        Check("rich: bold, link and list become HTML", html.Contains("<b>you</b>") && html.Contains("<a href=\"https://example.com/\">site</a>") && html.Contains("<ul><li>one</li></ul>") && html.Contains("&amp;"));
        Check("rich: unsafe links are dropped", !html.Contains("javascript"));
        Check("rich: formatting detected", RichTextConvert.HasFormatting(doc));
        var plainDoc = new System.Windows.Documents.FlowDocument();
        RichTextConvert.LoadPlain(plainDoc, "a\nb");
        Check("rich: plain text stays plain", !RichTextConvert.HasFormatting(plainDoc) && RichTextConvert.Plain(plainDoc) == "a\nb");
        Check("rich: RTF round trip", RichTextConvert.Rtf(doc).StartsWith("{\\rtf"));

        Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", System.IO.Path.Combine(Out, "editor-data"));
        var settings = new Settings();
        var lib = new SnippetLibrary(settings);
        var ed = SnippetEditor.Open(lib, settings);
        ed.Left = -32000; ed.Top = -32000;
        Pump(500);

        System.Windows.Controls.Button Btn(string text) => Find<System.Windows.Controls.Button>(ed, b => b.Content as string == text)!;
        System.Windows.Controls.TextBox Box(string name) => Find<System.Windows.Controls.TextBox>(ed, t => Name(t) == name)!;
        var body = Find<System.Windows.Controls.RichTextBox>(ed, r => Name(r) == "Text to insert")!;
        bool ErrorShown(string part) => Find<System.Windows.Controls.TextBlock>(ed, t => t.IsVisible && t.Text.Contains(part)) != null;

        Check("editor: opens with the example snippets listed", Find<System.Windows.Controls.ListBox>(ed, l => Name(l) == "Snippets")!.Items.Count >= 3);

        Invoke(Btn("New snippet"));
        Box("Short code").Text = "addr";
        Box("Name").Text = "Address";
        RichTextConvert.LoadPlain(body.Document, "123 Example Street\nSpringfield");
        Invoke(Btn("Save"));
        Check("editor: new snippet saved", lib.Matcher.Find("addr")?.Body == "123 Example Street\nSpringfield" && lib.Matcher.Find("addr")?.Label == "Address");

        Invoke(Btn("New snippet"));
        Box("Short code").Text = "bad code";
        RichTextConvert.LoadPlain(body.Document, "x");
        Invoke(Btn("Save"));
        Check("editor: bad short code is explained, not saved", ErrorShown("no spaces") && lib.Matcher.Find("bad") == null);
        Box("Short code").Text = "addr";
        Invoke(Btn("Save"));
        Check("editor: duplicate short code is refused", ErrorShown("already uses") && lib.Snippets.Count(x => x.Abbreviation == "addr") == 1);
        Box("Short code").Text = "hi";
        body.Document.Blocks.Clear();
        Invoke(Btn("Save"));
        Check("editor: empty text is refused", ErrorShown("Add the text"));
        body.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Bold(new System.Windows.Documents.Run("Hello"))));
        Invoke(Btn("Save"));
        var hi = lib.Snippets.FirstOrDefault(x => x.Abbreviation == "hi");
        Check("editor: formatted snippet saved as rich", hi != null && hi.IsRich && (hi.Html ?? "").Contains("<b>Hello</b>") && hi.Body == "Hello");

        // Optional picture of the editor for a visual check: set EDITOR_SHOT to a .png path
        if (Environment.GetEnvironmentVariable("EDITOR_SHOT") is { Length: > 0 } shot)
        {
            var root = (System.Windows.Controls.Panel)ed.Content;
            root.Background = ed.Background;
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(shot);
            png.Save(fs);
        }

        ed.Close();
        Pump(200);
        lib.Dispose();
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

        var quiet = Find<System.Windows.Controls.Primitives.ToggleButton>(f, t => Name(t) == "Stay quiet in apps with emoji popups")!;
        Check("quiet in emoji popup apps is on by default", quiet.IsChecked == true && host.Settings.QuietInShortcodeApps);
        Invoke(quiet);
        Check("quiet in emoji popup apps can be turned off", !host.Settings.QuietInShortcodeApps);
        Invoke(quiet);

        var titleBox = (System.Windows.Controls.TextBox)f.FindName("TitleBox");
        var titleAdd = Find<System.Windows.Controls.Button>(f, b => Name(b) == "Add ignored site")!;
        titleBox.Text = "j"; Invoke(titleAdd);
        Check("one letter site is not added", host.Settings.IgnoredTitles.Count == 0);
        titleBox.Text = "  Jira "; Invoke(titleAdd);
        Check("ignored site added and trimmed", host.Settings.IgnoredTitles.SequenceEqual(new[] { "Jira" }) && titleBox.Text == "");
        titleBox.Text = "jira"; Invoke(titleAdd);
        Check("same site is not added twice", host.Settings.IgnoredTitles.Count == 1);
        Check("ignored site shows as a chip", Find<System.Windows.Controls.Button>(f, b => Name(b) == "Remove Jira") != null);
        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Remove Jira")!);
        Pump(300);
        Check("removing the site chip removes it", host.Settings.IgnoredTitles.Count == 0);
        titleBox.Text = "Jira"; Invoke(titleAdd);

        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Remove cmd")!);
        Pump(300);
        Check("removing a chip removes the app", !host.Settings.IgnoredApps.Contains("cmd"));
        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Ignore Discord")!);
        Check("adding the last app works", host.Settings.IgnoredApps.Contains("Discord"));
        Check("add chip is gone once added", Find<System.Windows.Controls.Button>(f, b => Name(b) == "Ignore Discord") == null);
        Invoke(Find<System.Windows.Controls.Button>(f, b => Name(b) == "Use in Discord")!);
        Check("paste mode app added", host.Settings.PasteModeApps.Contains("Discord"));

        var saved = File.ReadAllText(System.IO.Path.Combine(Out, "flyout-data", "settings.json"));
        Check("changes are saved to the settings file", saved.Contains("\"MaxResults\": 8") && saved.Contains("Discord") && saved.Contains("Jira"));

        var snipSwitch = Find<System.Windows.Controls.Primitives.ToggleButton>(f, t => Name(t) == "Snippets on or off")!;
        Check("snippets switch is on by default", snipSwitch.IsChecked == true && host.Settings.SnippetsEnabled);
        Invoke(snipSwitch);
        Check("snippets can be turned off", !host.Settings.SnippetsEnabled);
        Invoke(snipSwitch);
        Check("snippets back on", host.Settings.SnippetsEnabled);

        // Optional picture of the panel for a visual check: set FLYOUT_SHOT to a .png path
        if (Environment.GetEnvironmentVariable("FLYOUT_SHOT") is { Length: > 0 } shot)
        {
            var root = (FrameworkElement)f.Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = File.Create(shot);
            png.Save(fs);
        }

        Invoke(((System.Windows.Controls.Button)f.FindName("QuitButton")));
        Check("quit closes the panel and quits", host.Quitted && f.IsClosing);
        // "Edit snippets" closes the panel and opens the editor
        var f2 = new TrayFlyout(host, -30000) { Left = -32000, Top = -32000, ShowActivated = false };
        f2.Show(); Pump(300);
        Invoke(Find<System.Windows.Controls.Button>(f2, b => Name(b) == "Edit snippets")!);
        Check("edit snippets opens the editor", host.SnippetsOpened && f2.IsClosing);

        EditorTests(Check);
        Console.WriteLine($"RESULT pass={pass} fail={fail}");
        return fail == 0 ? 0 : 1;
    }

}
