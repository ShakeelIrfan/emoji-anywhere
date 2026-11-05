using System.Diagnostics;
using System.Windows.Automation;
using EmojiAnywhere.Core;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

// Decides if the popup is allowed in the current window.
public static class ContextGuard
{
    private static IntPtr _lastHwnd;
    private static string _lastName = "";

    public static bool ShouldIgnore(IntPtr hwnd, Settings settings)
    {
        string name = ProcessName(hwnd);
        if (name.Length == 0) return true; // could not read it, likely an admin window
        if (name.Equals("EmojiAnywhere", StringComparison.OrdinalIgnoreCase)) return false;
        if (settings.IgnoredApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return IsPasswordField();
    }

    public static string ProcessName(IntPtr hwnd)
    {
        if (hwnd == _lastHwnd) return _lastName;
        try
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            _lastName = p.ProcessName;
        }
        catch { _lastName = ""; }
        _lastHwnd = hwnd;
        return _lastName;
    }

    private static bool IsPasswordField()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            return el != null && el.Current.IsPassword;
        }
        catch
        {
            return false;
        }
    }
}
