using System.Text.RegularExpressions;

namespace EmojiAnywhere.Core;

// Window title checks. Browsers put the tab title in the window title, so these can skip one site and not the whole browser.
public static class TitleRules
{
    public static readonly string[] Browsers = { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "Arc", "chromium", "librewolf", "zen", "waterfox" };

    // True when the title contains one of the person's rules (case does not matter)
    public static bool Ignored(string title, IEnumerable<string> rules) =>
        title.Length > 0 && rules.Any(r => r.Length > 0 && title.Contains(r, StringComparison.OrdinalIgnoreCase));

    // True when one whole part of the title is a site with its own emoji popup.
    // "ABC-12 Fix login - Jira - Google Chrome" has the part "Jira"; "Notes on Jira pricing" does not.
    public static bool IsShortcodeSite(string title)
    {
        // Edge adds "and 3 more pages" when a window has several tabs
        title = Regex.Replace(title, @" and \d+ more pages?", "");
        var parts = title.Split(new[] { " - ", " | ", " – ", " — " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => Settings.ShortcodeSites.Contains(p, StringComparer.OrdinalIgnoreCase));
    }
}
