using System.Runtime.InteropServices;
using System.Windows;
using EmojiAnywhere.Core;
using EmojiAnywhere.Native;
using EmojiAnywhere.UI;

namespace EmojiAnywhere.Input;

// Puts a snippet into the app you are typing in. Runs on the UI thread because it may read the clipboard
// for {clipboard} and may show a small form for {input:...} and {choice:...} fields first.
public static class SnippetInserter
{
    public static void Insert(Snippet s, SnippetMatcher matcher, IntPtr window, int deleteCount, string after, bool enterAfter, bool appPaste, string swallowed)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var content = Content(s);
        var fields = SnippetText.Fields(SnippetText.ResolveNested(content.Plain, matcher.FindAny));
        if (fields.Count > 0)
        {
            var filled = SnippetForm.Ask(s.Label.Length > 0 ? s.Label : s.Abbreviation, fields);
            Focus(window);
            if (filled == null)
            {
                // Cancelled: give back the key we held back, so the text looks like nothing happened
                if (swallowed.Length > 0) Delay(() => TextInjector.Type(swallowed));
                return;
            }
            foreach (var kv in filled) values[kv.Key] = kv.Value;
            Delay(() => Put(content, matcher, values, deleteCount, after, enterAfter, appPaste)); // let the app take focus back first
            return;
        }
        Put(content, matcher, values, deleteCount, after, enterAfter, appPaste);
    }

    private sealed record Parts(string Plain, string? Html, string? Rtf);

    // Formatted snippets are rebuilt from their RTF every time, through the same converter the editor uses.
    // So what gets pasted always matches what the editor shows, unsafe links are dropped, and anything
    // else hidden in a synced or imported file (embedded objects, odd HTML) never reaches the app.
    private static Parts Content(Snippet s)
    {
        if (!s.IsRich) return new Parts(s.Body, null, null);
        try
        {
            var doc = new System.Windows.Documents.FlowDocument();
            RichTextConvert.LoadRtf(doc, s.Rtf!);
            return new Parts(RichTextConvert.Plain(doc), RichTextConvert.Html(doc), RichTextConvert.Rtf(doc));
        }
        catch { return new Parts(s.Body, null, null); }
    }

    private static void Put(Parts s, SnippetMatcher matcher, Dictionary<string, string> values, int deleteCount, string after, bool enterAfter, bool appPaste)
    {
        var now = DateTime.Now;
        Func<string> clip = () => { try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; } };
        var plain = SnippetText.Render(s.Plain, values, clip, now, matcher.FindAny);
        int left = plain.CursorBack;
        if (left > 0)
        {
            left += after.Length;   // the space that ended it goes in after the text, so step over it too
            enterAfter = false;     // pressing Enter and then moving the cursor would make no sense
        }

        if (s.Html != null || s.Rtf != null)
        {
            string? html = s.Html == null ? null : SnippetText.RenderHtml(s.Html, values, clip, now, matcher.FindAny);
            string? rtf = s.Rtf == null ? null : SnippetText.RenderRtf(s.Rtf, values, clip, now, matcher.FindAny);
            TextInjector.ReplaceRich(deleteCount, plain.Text, html, rtf, after, enterAfter, left);
            return;
        }

        // Long or multi line text goes in through the clipboard: typing it would be slow, and typing a new line
        // presses Enter, which sends the message in chat apps
        bool paste = appPaste || plain.Text.Contains('\n') || plain.Text.Length > 300;
        string text = plain.Text;
        if (paste) TextInjector.Replace(deleteCount, text, usePaste: true, after, enterAfter, left);
        else Task.Run(() => TextInjector.Replace(deleteCount, text, usePaste: false, after, enterAfter, left));
    }

    private static void Delay(Action a)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
    }

    // Gives the keyboard back to the app the snippet was typed in
    public static void Focus(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return;
        uint target = Win32.GetWindowThreadProcessId(window, out _);
        uint me = GetCurrentThreadId();
        bool attached = target != me && AttachThreadInput(me, target, true);
        SetForegroundWindow(window);
        if (attached) AttachThreadInput(me, target, false);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
