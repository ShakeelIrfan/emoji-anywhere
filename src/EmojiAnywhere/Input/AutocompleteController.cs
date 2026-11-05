using System.Windows.Threading;
using EmojiAnywhere.Core;
using EmojiAnywhere.Native;
using EmojiAnywhere.UI;

namespace EmojiAnywhere.Input;

// The brain. Watches keys, decides when to open the popup, and inserts the emoji.
// The only typed text it keeps is the short ":query" while it is being typed.
//
// Threads: OnKey runs on the keyboard hook thread and must stay fast. Anything slow
// (drawing the popup, finding the caret, checking for password fields) is handed to the
// UI thread. All shared state is guarded by _gate.
public sealed class AutocompleteController
{
    private readonly EmojiIndex _index;
    private readonly UsageStore _usage;
    private readonly Settings _settings;
    private readonly SuggestionPopup _popup;
    private readonly Dispatcher _ui;
    private readonly object _gate = new();

    private bool _atBoundary = true;      // last char was a space, a new line, or nothing yet
    private bool _collecting;             // we saw a ":" at a boundary
    private string _query = "";
    private IntPtr _window;               // window the ":" was typed in
    private IntPtr _lastForeground;
    private List<EmojiEntry> _results = new();
    private bool _open;                   // popup is showing (or about to)
    private int _selected;
    private int _session;                 // bumps on every reset, so late UI work can tell it is stale

    public bool Paused { get; set; }

    // The last window you typed in, so the tray menu can offer "Ignore this app"
    public IntPtr LastTypedWindow => _lastForeground;

    public AutocompleteController(EmojiIndex index, UsageStore usage, Settings settings, SuggestionPopup popup)
    {
        _index = index; _usage = usage; _settings = settings; _popup = popup;
        _ui = Dispatcher.CurrentDispatcher;
        _popup.ItemClicked += i =>
        {
            lock (_gate) { if (_open && i < _results.Count) Commit(_results[i]); }
        };
    }

    // Returns true to swallow the key
    public bool OnKey(KeyPress k)
    {
        lock (_gate)
        {
            if (!_settings.Enabled || Paused) return false;

            IntPtr fg = Win32.GetForegroundWindow();
            if (fg != _lastForeground)
            {
                // Switched to another window: we know nothing about what is before the cursor there
                ResetLocked();
                _atBoundary = true;
                _lastForeground = fg;
            }

            // Shortcuts like Ctrl+V or Win+Tab mean we no longer know what is in the text box
            bool altGr = k.Ctrl && k.Alt && k.Char != null; // AltGr types real characters on many layouts
            if (!altGr && (k.Ctrl || k.Alt || k.Win))
            {
                if (IsModifier(k.Vk)) return false; // just pressing Shift, Ctrl or Alt changes nothing
                // After Ctrl+A, Ctrl+V and similar we can't know what is before the cursor.
                // Allow the popup: it still needs ":" plus two letters to show anything.
                ResetLocked(); _atBoundary = true;
                return false;
            }

            if (_collecting && _open && HandlePopupKey(k)) return true;

            if (!_collecting)
            {
                if (k.Char == ':' && _atBoundary)
                {
                    _collecting = true; _query = ""; _window = fg;
                    _atBoundary = false;
                    int session = _session;
                    // Checked on the UI thread, so typing never waits on it
                    _ui.BeginInvoke(() =>
                    {
                        if (!ContextGuard.ShouldIgnore(fg, _settings)) return;
                        lock (_gate) { if (_session == session) ResetLocked(); }
                    });
                    return false;
                }
                TrackBoundary(k);
                return false;
            }

            // Collecting a query
            if (k.Vk == Win32.VK_BACK)
            {
                if (_query.Length == 0) { ResetLocked(); _atBoundary = true; return false; }
                _query = _query[..^1];
                UpdateResults();
                return false;
            }

            if (k.Char is char c)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '+' || c == '-')
                {
                    _query += char.ToLowerInvariant(c);
                    UpdateResults();
                    return false;
                }
                if (c == ':' && _query.Length > 0 && _index.FindExact(_query) is EmojiEntry exact)
                {
                    Commit(exact);
                    return true; // swallow the closing ":"
                }
            }

            if (IsModifier(k.Vk)) return false;

            // Anything else ends the query
            ResetLocked();
            TrackBoundary(k);
            return false;
        }
    }

    private bool HandlePopupKey(KeyPress k)
    {
        switch (k.Vk)
        {
            case Win32.VK_DOWN: Select((_selected + 1) % _results.Count); return true;
            case Win32.VK_UP: Select((_selected - 1 + _results.Count) % _results.Count); return true;
            case Win32.VK_RETURN:
            case Win32.VK_TAB:
                Commit(_results[_selected]);
                return true;
            case Win32.VK_ESCAPE:
                ResetLocked(); return true;
        }
        return false;
    }

    private void Select(int index)
    {
        _selected = index;
        int session = _session;
        _ui.BeginInvoke(() => { lock (_gate) { if (_session != session) return; } _popup.Selected = index; });
    }

    private void UpdateResults()
    {
        _results = _query.Length < 2 ? new List<EmojiEntry>() : _index.Search(_query, _settings.MaxResults);
        _selected = 0;
        _open = _results.Count > 0;
        int session = _session;
        var results = _results;
        var window = _window;
        double scale = _settings.PopupScale;

        _ui.BeginInvoke(() =>
        {
            lock (_gate) { if (_session != session || !ReferenceEquals(_results, results)) return; } // newer keys already replaced this
            if (results.Count == 0) { _popup.Hide(); return; }
            _popup.Show(results, CaretLocator.Find(window), scale);
        });
    }

    private void Commit(EmojiEntry emoji)
    {
        // Delete ":" plus the query. Enter, Tab or the closing ":" were swallowed, so they never reached the app.
        int toDelete = 1 + _query.Length;
        bool paste = _settings.PasteModeApps.Contains(ContextGuard.ProcessName(_window), StringComparer.OrdinalIgnoreCase);
        _usage.Record(emoji.Hex);
        ResetLocked();
        _atBoundary = false;

        // Runs after the hook returns, so the swallowed key is fully handled first
        if (paste) _ui.BeginInvoke(() => TextInjector.Replace(toDelete, emoji.Unicode, usePaste: true));
        else Task.Run(() => TextInjector.Replace(toDelete, emoji.Unicode, usePaste: false));
    }

    private void TrackBoundary(KeyPress k)
    {
        if (k.Vk == Win32.VK_RETURN || k.Char is ' ' or '\t' or '\r' or '\n') _atBoundary = true;
        else if (k.Vk is Win32.VK_BACK or 0x2E /* Delete */) _atBoundary = true; // unknown what is left, so allow it
        else if (k.Char != null) _atBoundary = false;
        else if (k.Vk is Win32.VK_LEFT or Win32.VK_RIGHT or Win32.VK_UP or Win32.VK_DOWN) _atBoundary = false;
    }

    private static bool IsModifier(int vk) =>
        vk is Win32.VK_SHIFT or Win32.VK_CONTROL or Win32.VK_MENU or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or Win32.VK_CAPITAL;

    public void Reset()
    {
        lock (_gate) ResetLocked();
    }

    // A click anywhere except our popup means the cursor may have moved: start fresh
    public void OnClick()
    {
        Win32.GetCursorPos(out var p);
        IntPtr hit = WindowFromPoint(p);
        if (hit != IntPtr.Zero && hit == _popup.Hwnd) return;
        lock (_gate) { ResetLocked(); _atBoundary = true; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32.POINT p);

    private void ResetLocked()
    {
        bool wasOpen = _open;
        _collecting = false;
        _query = "";
        _results = new List<EmojiEntry>();
        _open = false;
        _selected = 0;
        _session++;
        if (wasOpen) _ui.BeginInvoke(() => _popup.Hide());
    }
}
