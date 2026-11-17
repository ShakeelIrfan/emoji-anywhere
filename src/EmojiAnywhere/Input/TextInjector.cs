using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

// Deletes the ":query" (or ";abbr") and puts the emoji or snippet text in its place.
// "after" is typed once the text is in (the space or comma that ended a snippet); enterAfter presses Enter.
public static class TextInjector
{
    // leftAfter: press Left this many times at the end, to put the cursor where a snippet's {cursor} was
    public static void Replace(int backspaces, string text, bool usePaste, string after = "", bool enterAfter = false, int leftAfter = 0)
    {
        var inputs = new List<Win32.INPUT>();
        for (int i = 0; i < backspaces; i++) AddKey(inputs, Win32.VK_BACK);

        if (!usePaste)
        {
            foreach (char ch in text + after) AddUnicode(inputs, ch);
            AddTail(inputs, "", enterAfter, leftAfter);
            Send(inputs);
            return;
        }

        Send(inputs);
        PasteAndRestore(new DataObject(DataFormats.UnicodeText, text.ReplaceLineEndings("\r\n")));
        var tail = new List<Win32.INPUT>();
        AddTail(tail, after, enterAfter, leftAfter);
        Send(tail);
    }

    // Formatted text (bold, links, lists): pasted as HTML for browsers, RTF for Word, and plain text for the rest
    public static void ReplaceRich(int backspaces, string plain, string? html, string? rtf, string after, bool enterAfter, int leftAfter)
    {
        var inputs = new List<Win32.INPUT>();
        for (int i = 0; i < backspaces; i++) AddKey(inputs, Win32.VK_BACK);
        Send(inputs);
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, plain.ReplaceLineEndings("\r\n"));
        if (html != null) data.SetData(DataFormats.Html, Core.SnippetText.ClipboardHtml(html));
        if (rtf != null) data.SetData(DataFormats.Rtf, rtf);
        PasteAndRestore(data);
        var tail = new List<Win32.INPUT>();
        AddTail(tail, after, enterAfter, leftAfter);
        Send(tail);
    }

    // Types text back, for example a key we held back when a fill in form was cancelled
    public static void Type(string text)
    {
        var inputs = new List<Win32.INPUT>();
        foreach (char ch in text) AddUnicode(inputs, ch);
        Send(inputs);
    }

    private static void AddTail(List<Win32.INPUT> list, string after, bool enterAfter, int leftAfter)
    {
        foreach (char ch in after) AddUnicode(list, ch);
        if (enterAfter) AddKey(list, Win32.VK_RETURN);
        for (int i = 0; i < Math.Min(leftAfter, 5000); i++) AddKey(list, Win32.VK_LEFT);
    }

    private static void PasteAndRestore(DataObject temp)
    {
        // Take a real copy of what is on the clipboard. GetDataObject alone is only a live
        // view of the clipboard, so it would show the emoji by the time we restore it.
        DataObject? saved = null;
        try
        {
            var live = Clipboard.GetDataObject();
            if (live != null)
            {
                saved = new DataObject();
                foreach (string format in live.GetFormats(false))
                {
                    try { var data = live.GetData(format, false); if (data != null) saved.SetData(format, data); } catch { }
                }
            }
        }
        catch { }

        // Neither the temporary emoji nor the restored content should show up as a new entry in
        // Windows clipboard history or cloud clipboard. This also keeps a copied password private:
        // password managers mark them this way, and the marks could otherwise be lost on restore.
        if (saved != null) MarkPrivate(saved);
        MarkPrivate(temp);
        try { Clipboard.SetDataObject(temp, true); } catch { return; }
        uint ourSequence = GetClipboardSequenceNumber();

        var paste = new List<Win32.INPUT>();
        paste.Add(Key(Win32.VK_CONTROL, false));
        paste.Add(Key(Win32.VK_V, false));
        paste.Add(Key(Win32.VK_V, true));
        paste.Add(Key(Win32.VK_CONTROL, true));
        Send(paste);

        // Give the app time to read the clipboard before we put the old content back
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // If anything copied something new in the meantime, leave it alone
            if (GetClipboardSequenceNumber() != ourSequence) return;
            try { if (saved != null) Clipboard.SetDataObject(saved, true); else Clipboard.Clear(); } catch { }
        };
        timer.Start();
    }

    private static void MarkPrivate(DataObject data)
    {
        static MemoryStream Zero() => new(new byte[4]);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", Zero());
        data.SetData("CanIncludeInClipboardHistory", Zero());
        data.SetData("CanUploadToCloudClipboard", Zero());
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private static void AddKey(List<Win32.INPUT> list, int vk)
    {
        list.Add(Key(vk, false));
        list.Add(Key(vk, true));
    }

    private static void AddUnicode(List<Win32.INPUT> list, char ch)
    {
        foreach (bool up in new[] { false, true })
        {
            list.Add(new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.InputUnion
                {
                    ki = new Win32.KEYBDINPUT { wScan = ch, dwFlags = Win32.KEYEVENTF_UNICODE | (up ? Win32.KEYEVENTF_KEYUP : 0), dwExtraInfo = Win32.OurMarker }
                }
            });
        }
    }

    private static Win32.INPUT Key(int vk, bool up) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? Win32.KEYEVENTF_KEYUP : 0, dwExtraInfo = Win32.OurMarker } }
    };

    private static void Send(List<Win32.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Win32.INPUT>());
    }
}
