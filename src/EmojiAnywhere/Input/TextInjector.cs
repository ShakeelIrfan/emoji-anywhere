using System.Runtime.InteropServices;
using System.Windows;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

// Deletes the ":query" and puts the emoji in its place.
public static class TextInjector
{
    public static void Replace(int backspaces, string emoji, bool usePaste)
    {
        var inputs = new List<Win32.INPUT>();
        for (int i = 0; i < backspaces; i++) AddKey(inputs, Win32.VK_BACK);

        if (!usePaste)
        {
            foreach (char ch in emoji) AddUnicode(inputs, ch);
            Send(inputs);
            return;
        }

        Send(inputs);
        PasteAndRestore(emoji);
    }

    private static void PasteAndRestore(string emoji)
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

        try { Clipboard.SetText(emoji); } catch { return; }

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
            try { if (saved != null) Clipboard.SetDataObject(saved, true); else Clipboard.Clear(); } catch { }
        };
        timer.Start();
    }

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
