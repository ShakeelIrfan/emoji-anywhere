using System.Windows.Threading;
using EmojiAnywhere.Core;
using EmojiAnywhere.Native;
using EmojiAnywhere.UI;

namespace EmojiAnywhere.Input;

// The brain. Watches keys, decides when to open the popup, and inserts the emoji.
// The only typed text it keeps is the short ":query" while it is being typed.
public sealed class AutocompleteController
{
    private readonly EmojiIndex _index;
    private readonly UsageStore _usage;
    private readonly Settings _settings;
    private readonly SuggestionPopup _popup;
    private readonly Dispatcher _ui;

    private bool _atBoundary = true;      // last char was a space, a new line, or nothing yet
    private bool _collecting;             // we saw a ":" at a boundary
    private string _query = "";
    private IntPtr _window;               // window the ":" was typed in
    private List<EmojiEntry> _results = new();

    public bool Paused { get; set; }

    public AutocompleteController(EmojiIndex index, UsageStore usage, Settings settings, SuggestionPopup popup)
    {
        _index = index; _usage = usage; _settings = settings; _popup = popup;
        _ui = Dispatcher.CurrentDispatcher;
        _popup.ItemClicked += i => { _popup.Selected = i; Commit(_results[i], typedClosingColon: false); };
    }

    // Returns true to swallow the key
    public bool OnKey(KeyPress k)
    {
        if (!_settings.Enabled || Paused) return false;

        IntPtr fg = Win32.GetForegroundWindow();
        if (_collecting && fg != _window) Reset();

        // Shortcuts like Ctrl+V or Win+Tab mean we no longer know what is in the text box
        bool altGr = k.Ctrl && k.Alt && k.Char != null; // AltGr types real characters on many layouts
        if (!altGr && (k.Ctrl || k.Alt || k.Win))
        {
            Reset(); _atBoundary = false;
            return false;
        }

        if (_collecting && _popup.IsOpen && HandlePopupKey(k)) return true;

        if (!_collecting)
        {
            if (k.Char == ':' && _atBoundary)
            {
                _collecting = true; _query = ""; _window = fg;
                _atBoundary = false;
                // Checked after the key goes through, so typing never waits on it
                _ui.BeginInvoke(() => { if (_window == fg && ContextGuard.ShouldIgnore(fg, _settings)) Reset(); });
                return false;
            }
            TrackBoundary(k);
            return false;
        }

        // Collecting a query
        if (k.Vk == Win32.VK_BACK)
        {
            if (_query.Length == 0) { Reset(); _atBoundary = true; return false; }
            _query = _query[..^1];
            _ui.BeginInvoke(Refresh);
            return false;
        }

        if (k.Char is char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '+' || c == '-')
            {
                _query += char.ToLowerInvariant(c);
                _ui.BeginInvoke(Refresh);
                return false;
            }
            if (c == ':' && _query.Length > 0 && _index.FindExact(_query) is EmojiEntry exact)
            {
                Commit(exact, typedClosingColon: true);
                return true; // swallow the closing ":"
            }
        }

        // Anything else ends the query
        Reset();
        TrackBoundary(k);
        return false;
    }

    private bool HandlePopupKey(KeyPress k)
    {
        switch (k.Vk)
        {
            case Win32.VK_DOWN: _popup.Selected = (_popup.Selected + 1) % _results.Count; return true;
            case Win32.VK_UP: _popup.Selected = (_popup.Selected - 1 + _results.Count) % _results.Count; return true;
            case Win32.VK_RETURN:
            case Win32.VK_TAB:
                Commit(_results[_popup.Selected], typedClosingColon: false);
                return true;
            case Win32.VK_ESCAPE:
                Reset(); return true;
        }
        return false;
    }

    private void Refresh()
    {
        if (!_collecting) return;
        if (_query.Length < 2) { _popup.Hide(); return; }
        _results = _index.Search(_query, _settings.MaxResults);
        if (_results.Count == 0) { _popup.Hide(); return; }
        _popup.Show(_results, CaretLocator.Find(_window), _settings.PopupScale);
    }

    private void Commit(EmojiEntry emoji, bool typedClosingColon)
    {
        // Delete ":" plus the query. The closing ":" was swallowed so it never reached the app.
        int toDelete = 1 + _query.Length;
        string processName = ContextGuard.ProcessName(_window);
        bool paste = _settings.PasteModeApps.Contains(processName, StringComparer.OrdinalIgnoreCase);
        _usage.Record(emoji.Hex);
        Reset();
        _atBoundary = false;
        // Run after the hook returns so the swallowed key is fully handled first
        _ui.BeginInvoke(() => TextInjector.Replace(toDelete, emoji.Unicode, paste));
    }

    private void TrackBoundary(KeyPress k)
    {
        if (k.Vk == Win32.VK_RETURN || k.Char is ' ' or '\t' or '\r' or '\n') _atBoundary = true;
        else if (k.Char != null) _atBoundary = false;
        else if (k.Vk is Win32.VK_LEFT or Win32.VK_RIGHT or Win32.VK_UP or Win32.VK_DOWN) _atBoundary = false;
    }

    public void Reset()
    {
        _collecting = false;
        _query = "";
        _results.Clear();
        _popup.Hide();
    }
}
