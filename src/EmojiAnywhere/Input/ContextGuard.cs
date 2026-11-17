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

    // forSnippets: snippets still work in apps with their own emoji popup (Discord, Slack), so those checks are skipped
    public static bool ShouldIgnore(IntPtr hwnd, Settings settings, bool forSnippets = false)
    {
        string name = ProcessName(hwnd);
        if (name.Length == 0) return true; // could not read it, likely an admin window
        if (name.Equals("EmojiAnywhere", StringComparison.OrdinalIgnoreCase)) return false;
        if (settings.IgnoredApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        bool quiet = settings.QuietInShortcodeApps && !forSnippets;
        if (quiet && Settings.ShortcodeApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        if (settings.IgnoredTitles.Count > 0 || (quiet && TitleRules.Browsers.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            string title = WindowTitle(hwnd);
            if (TitleRules.Ignored(title, settings.IgnoredTitles)) return true;
            if (quiet && TitleRules.Browsers.Contains(name, StringComparer.OrdinalIgnoreCase) && TitleRules.IsShortcodeSite(title)) return true;
        }
        return IsPasswordField();
    }

    public static string WindowTitle(IntPtr hwnd)
    {
        try
        {
            int len = Win32.GetWindowTextLength(hwnd);
            if (len <= 0) return "";
            var sb = new System.Text.StringBuilder(Math.Min(len, 1024) + 1);
            Win32.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch { return ""; }
    }

    private static readonly object _cacheLock = new();

    public static string ProcessName(IntPtr hwnd)
    {
        lock (_cacheLock)
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
