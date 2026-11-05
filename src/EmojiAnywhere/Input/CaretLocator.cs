using System.Windows.Automation;
using System.Windows.Automation.Text;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

// Finds where the text cursor is on screen, in physical pixels.
// Returns the bottom left of the caret, or the mouse position if no app tells us.
public static class CaretLocator
{
    public readonly record struct Spot(int X, int Y, bool FromMouse);

    public static Spot Find(IntPtr hwnd)
    {
        // 1. Classic Win32 caret: Notepad, Word, most native apps
        uint thread = Win32.GetWindowThreadProcessId(hwnd, out _);
        var gti = new Win32.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (Win32.GetGUIThreadInfo(thread, ref gti) && gti.hwndCaret != IntPtr.Zero)
        {
            var p = new Win32.POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
            if (Win32.ClientToScreen(gti.hwndCaret, ref p) && (p.X != 0 || p.Y != 0))
                return new Spot(p.X, p.Y, false);
        }

        // 2. UI Automation text pattern: Chrome, Edge, many modern apps
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el != null && el.TryGetCurrentPattern(TextPattern.Pattern, out object pat))
            {
                var sel = ((TextPattern)pat).GetSelection();
                if (sel.Length > 0)
                {
                    var range = sel[0];
                    var rects = range.GetBoundingRectangles();
                    if (rects.Length == 0)
                    {
                        // An empty selection has no box, so measure one character back
                        range = range.Clone();
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
                        rects = range.GetBoundingRectangles();
                    }
                    if (rects.Length > 0)
                    {
                        var r = rects[^1];
                        return new Spot((int)r.Right, (int)r.Bottom, false);
                    }
                }
            }
        }
        catch { }

        // 3. Fallback: near the mouse
        Win32.GetCursorPos(out var m);
        return new Spot(m.X, m.Y + 20, true);
    }
}
