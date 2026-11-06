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

        // 2. Accessibility caret: Chromium and Electron apps such as VS Code report the true cursor here,
        //    while their UI Automation text box can report a placeholder spot at the start of the line
        if (AccessibleCaret(gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : hwnd) is Spot acc)
            return acc;

        // 3. UI Automation text pattern: Chrome, Edge, many modern apps
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

        // 4. Fallback: near the mouse
        Win32.GetCursorPos(out var m);
        return new Spot(m.X, m.Y + 20, true);
    }

    private static Guid _iidAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    private const uint OBJID_CARET = 0xFFFFFFF8;

    private static Spot? AccessibleCaret(IntPtr hwnd)
    {
        object? obj = null;
        try
        {
            if (Win32.AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref _iidAccessible, out obj) != 0) return null;
            if (obj is not Accessibility.IAccessible acc) return null;
            acc.accLocation(out int left, out int top, out int width, out int height, 0);
            // An app with no caret answers with an empty box
            if (height <= 0 || (left == 0 && top == 0)) return null;
            return new Spot(left, top + height, false);
        }
        catch { return null; }
        finally { if (obj != null && System.Runtime.InteropServices.Marshal.IsComObject(obj)) System.Runtime.InteropServices.Marshal.ReleaseComObject(obj); }
    }
}
