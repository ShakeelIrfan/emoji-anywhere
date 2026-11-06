using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Live test for Emoji Anywhere.
// Types ONLY into its own test window. If any other window takes focus, it stops immediately.
static class Program
{
    static Form form = null!;
    static TextBox box = null!, pwd = null!;
    static Process? app;
    static readonly StringBuilder log = new();
    static int pass, fail;
    static bool aborted;
    static IntPtr focusTarget;
    static string browserKind = "edge";

    [STAThread]
    static int Main(string[] args)
    {
        string exe = args[0], appData = args[1];
        string mode = args.Length > 2 ? args[2] : "type";

        ApplicationConfiguration.Initialize();
        form = new Form { Text = "Emoji Anywhere live test", Width = 700, Height = 360, StartPosition = FormStartPosition.Manual, Left = 200, Top = 200, TopMost = true };
        box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14) };
        pwd = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Bottom, Font = new Font("Segoe UI", 14) };
        form.Controls.Add(box);
        form.Controls.Add(pwd);

        form.Shown += async (_, _) =>
        {
            try { await Run(exe, appData, mode); }
            catch (Exception ex) { Log("CRASH " + ex); fail++; }
            finally
            {
                try { app?.Kill(); } catch { }
                Console.WriteLine(log.ToString());
                Console.WriteLine($"RESULT pass={pass} fail={fail} aborted={aborted}");
                form.Close();
            }
        };
        Application.Run(form);
        return fail == 0 && !aborted ? 0 : 1;
    }

    static async Task Run(string exe, string appData, string mode)
    {
        Directory.CreateDirectory(Path.Combine(appData, "EmojiAnywhere"));
        if (mode == "paste")
            File.WriteAllText(Path.Combine(appData, "EmojiAnywhere", "settings.json"), "{\"PasteModeApps\":[\"" + Process.GetCurrentProcess().ProcessName + "\"]}");

        // A copy that is already running would make the test copy show its "already running" notice instead
        if (Process.GetProcessesByName("EmojiAnywhere").Length > 0) { Log("FAIL: quit the running Emoji Anywhere first"); fail++; return; }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.Environment["EMOJIANYWHERE_DATA"] = Path.Combine(appData, "EmojiAnywhere"); // keeps the test away from the real settings and usage files
        app = Process.Start(psi)!;
        await Task.Delay(2500);
        Log($"app started, pid {app.Id}, exited={app.HasExited}");
        if (app.HasExited) { fail++; return; }

        if (mode == "browser" || mode == "chrome") { browserKind = mode == "chrome" ? "chrome" : "edge"; await BrowserTests(appData); return; }
        if (mode == "vscode") { await VsCodeTests(appData); return; }

        if (!await Focus(box)) return;

        if (mode == "paste") { await PasteTests(); return; }

        await Case("full code", box, "hi :fire: ", "hi 🔥 ");
        await Case("enter picks first", box, ":sob{ENTER}", "😭");
        await Case("tab picks", box, ":joy{TAB}", "😂");
        await Case("arrow then enter", box, ":thumbs{DOWN}{ENTER}", null, expectNot: ":thumbs");
        await Case("time not triggered", box, "at 10:30 ok", "at 10:30 ok");
        await Case("mid word not triggered", box, "a:fire: b", "a:fire: b");
        await Case("esc closes", box, ":fi{ESC}re", ":fire");
        await Case("one letter no popup", box, ":f{ENTER}", ":f\r\n");
        await Case("backspace in query", box, ":firx{BACK}e:", "🔥");
        await Case("skin tone", box, ":thumbsup_tone3:", "👍🏽");
        await Case("unknown code untouched", box, ":notarealemoji: ", ":notarealemoji: ");
        await Case("new line start", box, "x{ENTER}:skull:", "x\r\n💀");
        await Case("capital letters", box, ":FIRE:", "🔥");

        await PopupPlacement();
        await Speed();

        if (!await Focus(pwd)) return;
        await Case("password field ignored", pwd, ":fire:", ":fire:");

        Log($"app RAM: working set {app.WorkingSet64 / 1048576.0:0.0} MB, private {app.PrivateMemorySize64 / 1048576.0:0.0} MB");
        app.Refresh();
        var cpu0 = app.TotalProcessorTime; await Task.Delay(5000); app.Refresh();
        Log($"app CPU while idle over 5 s: {(app.TotalProcessorTime - cpu0).TotalMilliseconds:0} ms");
        // The app trims its memory 30 seconds after the popup closes
        await Task.Delay(30000); app.Refresh();
        Log($"app RAM after 35 s idle: working set {app.WorkingSet64 / 1048576.0:0.0} MB, private {app.PrivateMemorySize64 / 1048576.0:0.0} MB");
    }

    // Tests in Microsoft Edge using a throwaway profile and a blank local page made for the test.
    // The page copies the text box into the window title so the test can read it.
    static async Task BrowserTests(string appData)
    {
        string dir = Path.Combine(appData, "browser");
        Directory.CreateDirectory(dir);
        string page = Path.Combine(dir, "test.html");
        File.WriteAllText(page, "<!doctype html><meta charset=utf-8><title>EATEST|</title>" +
            "<body style='margin:0'><textarea id=t autofocus style='width:100%;height:100vh;font-size:20px;box-sizing:border-box'></textarea>" +
            "<script>const t=document.getElementById('t');const u=()=>document.title='EATEST|'+t.value.replace(/\\n/g,'\\\\n').replace(/ /g,'~');" +
            "t.addEventListener('input',u);</script>");

        string edge = (browserKind == "chrome"
            ? new[] { @"C:\Program Files\Google\Chrome\Application\chrome.exe" }
            : new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }).FirstOrDefault(File.Exists) ?? "";
        if (edge == "") { Log("SKIP browser: Edge not found"); return; }
        var edgeProc = Process.Start(new ProcessStartInfo(edge,
            $"--user-data-dir=\"{Path.Combine(dir, "profile")}\" --no-first-run --no-default-browser-check --disable-sync --new-window --window-size=700,400 --window-position=200,200 --app=\"file:///{page.Replace('\\', '/')}\"") { UseShellExecute = false });

        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 40 && hwnd == IntPtr.Zero; i++) { await Task.Delay(250); hwnd = FindByTitlePrefix("EATEST|"); }
        if (hwnd == IntPtr.Zero) { Log("FAIL browser window did not open"); fail++; return; }
        await Task.Delay(1500);
        form.WindowState = FormWindowState.Minimized;
        focusTarget = hwnd;

        try
        {
            async Task<bool> FocusEdge()
            {
                SendKey(0x87, false); SendKey(0x87, true);
                SetForegroundWindow(hwnd);
                await Task.Delay(300);
                GetWindowRect(hwnd, out var r);
                await ClickScreen(r.Left + 60, r.Top + 120);
                await Task.Delay(200);
                if (GetForegroundWindow() != hwnd) { aborted = true; Log("ABORT: could not focus the Edge test window"); return false; }
                return true;
            }
            string Text() { var t = Title(hwnd); return t.StartsWith("EATEST|") ? t[7..].Replace("\\n", "\n").Replace("~", " ") : "?"; }

            async Task BCase(string name, string keys, string? expected)
            {
                if (aborted) return;
                if (!await FocusEdge()) return;
                await Type("{CTRLA}{BACK}");
                await Task.Delay(200);
                await Type(keys);
                await Task.Delay(700);
                string got = Text();
                Check(browserKind + ": " + name, got == expected, $"typed '{keys}' got '{got.Replace("\n", "\\n")}'");
                await Type("{ESC}");
            }

            await BCase("full code", "hi :fire: ", "hi 🔥 ");
            await BCase("enter picks first", ":sob{ENTER}", "😭");
            await BCase("tab picks", ":joy{TAB}", "😂");
            await BCase("time not triggered", "at 10:30 ok", "at 10:30 ok");
            await BCase("skin tone", ":thumbsup_tone3:", "👍🏽");

            // Where does the popup land? Near the text cursor, or did it fall back to the mouse?
            if (!aborted && await FocusEdge())
            {
                await Type("{CTRLA}{BACK}");
                await Type(":fir");
                await Task.Delay(500);
                var pop = AppWindows().FirstOrDefault();
                GetCursorPos(out var mouse);
                GetWindowRect(hwnd, out var er);
                if (pop == IntPtr.Zero) Check(browserKind + ": popup visible", false, "");
                else
                {
                    GetWindowRect(pop, out var pr);
                    Check(browserKind + ": popup visible", true, "");
                    Log($"{browserKind}: popup at {pr.Left},{pr.Top}; window top-left {er.Left},{er.Top}; mouse {mouse.X},{mouse.Y}");
                    bool atCaret = pr.Top < er.Top + 160 && Math.Abs(pr.Top - (mouse.Y + 20)) > 8;
                    Check(browserKind + ": popup at text cursor (not mouse fallback)", atCaret, "");
                }
                await Type("{ESC}");
            }
        }
        finally
        {
            focusTarget = IntPtr.Zero;
            PostMessage(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            await Task.Delay(1500);
            try { if (edgeProc != null && !edgeProc.HasExited) edgeProc.Kill(true); } catch { }
        }
    }

    // Tests in VS Code (Electron, like Discord and WhatsApp desktop) with a throwaway profile
    // and a test file made for it. Reads the result by saving the file.
    static async Task VsCodeTests(string appData)
    {
        string dir = Path.Combine(appData, "vscode");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "eatest.txt");
        File.WriteAllText(file, "");
        string code = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");
        if (!File.Exists(code)) { Log("SKIP vscode: not installed"); return; }

        var proc = Process.Start(new ProcessStartInfo(code,
            $"--user-data-dir \"{Path.Combine(dir, "ud")}\" --extensions-dir \"{Path.Combine(dir, "ext")}\" --new-window --disable-workspace-trust --skip-welcome --skip-release-notes --disable-extensions \"{file}\"") { UseShellExecute = false });

        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 80 && hwnd == IntPtr.Zero; i++) { await Task.Delay(250); hwnd = FindByTitlePrefix("eatest.txt"); }
        if (hwnd == IntPtr.Zero) { Log("FAIL vscode window did not open"); fail++; return; }
        await Task.Delay(3000);
        form.WindowState = FormWindowState.Minimized;
        focusTarget = hwnd;

        try
        {
            async Task<bool> FocusCode()
            {
                SendKey(0x87, false); SendKey(0x87, true);
                SetForegroundWindow(hwnd);
                await Task.Delay(300);
                GetWindowRect(hwnd, out var r);
                await ClickScreen(r.Left + (r.Right - r.Left) / 2, r.Top + (r.Bottom - r.Top) / 2);
                await Task.Delay(200);
                if (GetForegroundWindow() != hwnd) { aborted = true; Log($"ABORT: could not focus the VS Code test window (front: '{Title(GetForegroundWindow())}', test window: '{Title(hwnd)}')"); return false; }
                return true;
            }

            async Task CCase(string name, string keys, string expected)
            {
                if (aborted) return;
                if (!await FocusCode()) return;
                await Type("{CTRLA}{BACK}");
                await Task.Delay(200);
                await Type(keys);
                await Task.Delay(700);
                await Type("{ESC}{CTRLS}");
                await Task.Delay(800);
                string got = File.ReadAllText(file).Replace("\r\n", "\n");
                Check("vscode: " + name, got == expected, $"typed '{keys}' got '{got.Replace("\n", "\\n")}'");
            }

            await CCase("full code", "hi :fire: ", "hi 🔥 ");
            await CCase("enter picks first", ":sob{ENTER}", "😭");
            await CCase("tab picks", ":joy{TAB}", "😂");
            await CCase("time not triggered", "at 10:30 ok", "at 10:30 ok");
            await CCase("skin tone", ":thumbsup_tone3:", "👍🏽");

            int startLeft = int.MinValue;
            if (!aborted && await FocusCode())
            {
                await Type("{CTRLA}{BACK}");
                await Type(":fir");
                await Task.Delay(500);
                var pop = AppWindows().FirstOrDefault();
                GetCursorPos(out var mouse);
                GetWindowRect(hwnd, out var wr);
                if (pop == IntPtr.Zero) Check("vscode: popup visible", false, "");
                else
                {
                    GetWindowRect(pop, out var pr);
                    Check("vscode: popup visible", true, "");
                    Log($"vscode: popup at {pr.Left},{pr.Top}; window {wr.Left},{wr.Top} to {wr.Right},{wr.Bottom}; mouse {mouse.X},{mouse.Y}");
                    LogCaretSources(hwnd);
                    Check("vscode: popup at text cursor (not mouse fallback)", Math.Abs(pr.Top - (mouse.Y + 20 - 6)) > 8 || Math.Abs(pr.Left - (mouse.X - 12)) > 8, "");
                    // ":fir" sits at the start of the first line, so the popup belongs in the left part of the editor near the top
                    int width = wr.Right - wr.Left, height = wr.Bottom - wr.Top;
                    Check("vscode: popup next to the typed text", pr.Left < wr.Left + width * 0.45 && pr.Top < wr.Top + height * 0.4, $"popup {pr.Left},{pr.Top}");
                    startLeft = pr.Left;
                }
                await Type("{ESC}{CTRLA}{BACK}{CTRLS}");
            }

            // Middle of a long line: the popup must follow the cursor, not jump to the end of the line
            if (!aborted && await FocusCode())
            {
                await Type("{CTRLA}{BACK}");
                await Type("the quick brown fox jumps over the lazy dog and keeps running far far away from here", 10);
                await Type("{HOME}" + string.Concat(Enumerable.Repeat("{RIGHT}", 10)) + " :fir");
                await Task.Delay(500);
                var pop = AppWindows().FirstOrDefault();
                GetWindowRect(hwnd, out var wr);
                if (pop == IntPtr.Zero) Check("vscode: popup visible mid-line", false, "");
                else
                {
                    GetWindowRect(pop, out var pr);
                    Log($"vscode mid-line: popup at {pr.Left},{pr.Top}; window {wr.Left},{wr.Top} to {wr.Right},{wr.Bottom}");
                    LogCaretSources(hwnd);
                    int width = wr.Right - wr.Left, height = wr.Bottom - wr.Top;
                    // The cursor is 11 characters further right than in the line start case, so the popup must move right too
                    Check("vscode: popup follows the cursor mid-line", pr.Left > startLeft + 60 && pr.Left < wr.Left + width * 0.45 && pr.Top < wr.Top + height * 0.4, $"popup {pr.Left},{pr.Top}, line start popup at {startLeft}");
                }
                await Type("{ESC}{CTRLA}{BACK}{CTRLS}");
            }
        }
        finally
        {
            focusTarget = IntPtr.Zero;
            await Task.Delay(500);
            try { proc?.Kill(true); } catch { }
            foreach (var p in Process.GetProcessesByName("Code"))
            {
                try { if (p.MainWindowHandle == hwnd) p.Kill(true); } catch { }
            }
        }
    }

    // Logs what each caret source reports for a window, to see which one an app gets wrong
    static void LogCaretSources(IntPtr hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out _);
        var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(thread, ref gti))
        {
            var p = new POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
            ClientToScreen(gti.hwndCaret, ref p);
            Log($"  win32 caret: hwndCaret={gti.hwndCaret} rc={gti.rcCaret.Left},{gti.rcCaret.Top},{gti.rcCaret.Right},{gti.rcCaret.Bottom} screen={p.X},{p.Y}");
        }
        try
        {
            var el = System.Windows.Automation.AutomationElement.FocusedElement;
            Log($"  uia focused: {el?.Current.ControlType.ProgrammaticName} '{el?.Current.Name}' class={el?.Current.ClassName} box={el?.Current.BoundingRectangle}");
            if (el != null && el.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out object pat))
            {
                var sel = ((System.Windows.Automation.TextPattern)pat).GetSelection();
                Log($"  uia selections: {sel.Length}");
                if (sel.Length > 0)
                {
                    Log($"  uia sel rects: {string.Join(" | ", sel[0].GetBoundingRectangles())}");
                    var r = sel[0].Clone();
                    r.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, System.Windows.Automation.Text.TextUnit.Character, -1);
                    Log($"  uia back1 text='{r.GetText(20)}' rects: {string.Join(" | ", r.GetBoundingRectangles())}");
                }
            }
            else Log("  uia: no TextPattern");
            if (el != null && el.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out object pat2))
            {
                var doc = ((System.Windows.Automation.TextPattern)pat2).DocumentRange;
                Log($"  uia doc text='{doc.GetText(200).Replace('\n', '|')}'");
            }
        }
        catch (Exception ex) { Log("  uia error: " + ex.Message); }
        try
        {
            IntPtr target = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : hwnd;
            var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            if (AccessibleObjectFromWindow(target, unchecked((uint)-8), ref iid, out object acc) == 0 && acc is Accessibility.IAccessible ia)
            {
                ia.accLocation(out int l, out int t, out int w, out int h, 0);
                Log($"  msaa caret: {l},{t},{w},{h} (focus hwnd {target})");
            }
            else Log("  msaa caret: none");
        }
        catch (Exception ex) { Log("  msaa error: " + ex.Message); }
    }
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint t, ref GUITHREADINFO g);
    [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);

    static IntPtr FindByTitlePrefix(string prefix)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) => { if (IsWindowVisible(h) && Title(h).StartsWith(prefix)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    static async Task PasteTests()
    {
        Clipboard.SetText("keep me");
        await Case("paste mode inserts", box, ":fire:", "🔥");
        await Task.Delay(800);
        string clip = Clipboard.ContainsText() ? Clipboard.GetText() : "(empty)";
        Check("paste mode restores clipboard", clip == "keep me", $"clipboard='{clip}'");
    }

    static async Task PopupPlacement()
    {
        box.Clear(); await Click(box, 20, 20); await Task.Delay(100);
        await Type(":fir");
        await Task.Delay(400);
        var popup = AppWindows().FirstOrDefault();
        GetCaretPos(out var cp); ClientToScreen(box.Handle, ref cp);
        if (popup == IntPtr.Zero) { Check("popup visible", false, "no visible popup window"); }
        else
        {
            GetWindowRect(popup, out var r);
            bool near = Math.Abs(r.Left + 12 - cp.X) < 60 && r.Top > cp.Y - 10 && r.Top < cp.Y + 80;
            Check("popup visible", true, "");
            Check("popup at caret", near, $"popup {r.Left},{r.Top} caret {cp.X},{cp.Y}");
            Check("test window keeps focus", GetForegroundWindow() == form.Handle, "");
        }
        await Type("{ESC}");
        box.Clear();

        // Clicking the second row picks it
        await Click(box, 20, 20);
        await Type(":thumbs");
        await Task.Delay(500);
        var pop = AppWindows().FirstOrDefault();
        if (pop != IntPtr.Zero)
        {
            GetWindowRect(pop, out var pr);
            int rowH = (pr.Bottom - pr.Top - 32) / 6;
            await ClickScreen(pr.Left + 60, pr.Top + 16 + rowH + rowH / 2);
            await Task.Delay(600);
        }
        Check("click picks an emoji", box.Text.Length > 0 && !box.Text.Contains(":thumbs"), $"got '{box.Text}'");
        box.Clear();
    }

    static async Task Speed()
    {
        var times = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            box.Clear(); await Click(box, 20, 20); await Task.Delay(150);
            await Type(":fi");
            var sw = Stopwatch.StartNew();
            await Type("r", delay: 0);
            while (!AppWindows().Any() && sw.ElapsedMilliseconds < 1000) await Task.Delay(1);
            times.Add(sw.Elapsed.TotalMilliseconds);
            await Type("{ESC}");
            await Task.Delay(150);
        }
        Log($"popup appears after the 3rd letter in: {string.Join(", ", times.Select(t => t.ToString("0")))} ms");
        box.Clear();
    }

    static async Task Case(string name, TextBox target, string keys, string? expected, string? expectNot = null)
    {
        if (aborted) return;
        target.Clear(); await Click(target, 20, 20); await Task.Delay(150);
        await Type(keys);
        await Task.Delay(600);
        string got = target.Text;
        bool ok = expected != null ? got == expected : !got.Contains(expectNot!);
        Check(name, ok, $"typed '{keys}' got '{got.Replace("\r\n", "\\n")}'" + (expected != null ? $" expected '{expected.Replace("\r\n", "\\n")}'" : ""));
        await Type("{ESC}");
    }

    static async Task Click(Control c, int x, int y)
    {
        var p = c.PointToScreen(new Point(x, y));
        await ClickScreen(p.X, p.Y);
    }

    static async Task ClickScreen(int x, int y)
    {
        SetCursorPos(x, y);
        await Task.Delay(30);
        var down = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } };
        var up = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
        await Task.Delay(80);
    }

    static void Check(string name, bool ok, string detail)
    {
        if (ok) pass++; else fail++;
        Log($"{(ok ? "PASS" : "FAIL")} {name}  {detail}");
    }

    static async Task<bool> Focus(Control c)
    {
        // A tap of F24 (a key nothing uses) lets Windows hand focus to our window
        SendKey(0x87, false); SendKey(0x87, true);
        form.Activate(); SetForegroundWindow(form.Handle); c.Focus();
        await Task.Delay(400);
        if (GetForegroundWindow() != form.Handle) { aborted = true; Log("ABORT: could not focus the test window"); return false; }
        return true;
    }

    // Types text. {ENTER} {TAB} {ESC} {DOWN} {UP} {BACK} are special keys.
    static async Task Type(string keys, int delay = 35)
    {
        var layout = GetKeyboardLayout(0);
        for (int i = 0; i < keys.Length; i++)
        {
            if (aborted) return;
            if (GetForegroundWindow() != (focusTarget == IntPtr.Zero ? form.Handle : focusTarget)) { aborted = true; Log("ABORT: focus left the test window"); return; }
            int vk; bool shift = false;
            if (keys[i] == '{')
            {
                int end = keys.IndexOf('}', i);
                if (keys[(i + 1)..end] == "CTRLS") { SendKey(0x11, false); SendKey(0x53, false); SendKey(0x53, true); SendKey(0x11, true); i = end; await Task.Delay(delay); continue; }
                if (keys[(i + 1)..end] == "CTRLA") { SendKey(0x11, false); SendKey(0x41, false); SendKey(0x41, true); SendKey(0x11, true); i = end; await Task.Delay(delay); continue; }
                vk = keys[(i + 1)..end] switch { "ENTER" => 0x0D, "TAB" => 0x09, "ESC" => 0x1B, "DOWN" => 0x28, "UP" => 0x26, "BACK" => 0x08, "HOME" => 0x24, "RIGHT" => 0x27, _ => 0 };
                i = end;
            }
            else
            {
                short s = VkKeyScanEx(keys[i], layout);
                vk = s & 0xFF; shift = (s & 0x100) != 0;
            }
            if (shift) SendKey(0x10, false);
            SendKey(vk, false); SendKey(vk, true);
            if (shift) SendKey(0x10, true);
            if (delay > 0) await Task.Delay(delay); else Application.DoEvents();
        }
    }

    static IEnumerable<IntPtr> AppWindows()
    {
        var list = new List<IntPtr>();
        if (app == null) return list;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == app.Id && IsWindowVisible(h)) { GetWindowRect(h, out var r); if (r.Right - r.Left > 20) list.Add(h); }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static void Log(string s) { log.AppendLine(s); }

    static void SendKey(int vk, bool up)
    {
        var input = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? 2u : 0u } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] static extern short VkKeyScanEx(char c, IntPtr hkl);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint t);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetCaretPos(out POINT p);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
}
