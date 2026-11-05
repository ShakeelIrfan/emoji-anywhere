# Architecture

C# / .NET 8, WPF. One tray process, no main window.

## Threads

- **Hook thread.** Runs the low level keyboard and mouse hooks and nothing else. Windows waits for
  this thread before every keypress reaches any app, so it only does fast in-memory work
  (tracking the `:query`, searching, deciding whether to swallow a key).
- **UI thread.** Draws the popup, finds the text cursor, checks for password fields. Slow work here
  can never delay typing.
- Inserting the emoji runs on a worker thread, or on the UI thread in clipboard paste mode.

## Parts

| Part | Job |
|---|---|
| `KeyboardHook` | WH_KEYBOARD_LL and WH_MOUSE_LL on their own thread. Mouse hook only notes "a button was pressed". Skips only keys the app sends itself (marked with a tag), so on-screen keyboards still work. |
| `AutocompleteController` | Keeps the `:query` while it is typed, nothing else. Triggers only when `:` follows a space, a new line, a click or a window switch. Swallows arrows, Enter, Tab and Esc only while the popup is open. |
| `ContextGuard` | Skips password fields, ignored apps and windows it cannot read (admin windows). |
| `CaretLocator` | Finds the text cursor: GetGUIThreadInfo, then UI Automation. Falls back to the mouse position. |
| `EmojiIndex` | Built in emoji data with Discord, Slack and GitHub shortcodes plus keyword tags. In memory search ranked by exact, prefix, tag, popularity, then your usage and recency. |
| `SuggestionPopup` | WPF window that never takes focus (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW). Rounded, shadow, follows Windows light or dark mode. Software rendered to keep memory low. |
| `EmojiRenderer` | Draws color emoji with Direct2D and DirectWrite from the Windows emoji font, because WPF only draws them in black and white. Small cache of bitmaps. |
| `TextInjector` | Backspaces over `:query`, types the emoji with SendInput unicode. Clipboard paste mode copies the clipboard first and restores it after. |
| `TrayHost` | Tray icon: on/off, pause 1 hour, settings, quit. |
| `SettingsWindow` | Start with Windows, ignored apps, paste mode apps, result count, popup size. |
| `Storage` | JSON in %AppData%\EmojiAnywhere (or `EMOJIANYWHERE_DATA` if set): settings and emoji usage counts only. |
| `MemoryTrim` | A few seconds after the popup closes, frees memory and hands pages back to Windows. |

No networking code anywhere in the app.

## Measured (2026-11-05, Debug build, live test)

- Popup appears 3 to 6 ms after the key that opens it.
- About 8 MB of RAM in use while idle in the tray.
- No CPU use while idle.
