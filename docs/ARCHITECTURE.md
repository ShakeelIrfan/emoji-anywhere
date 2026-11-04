# Architecture

C# / .NET 8, WPF. One tray process, no main window.

| Part | Job |
|---|---|
| `KeyboardHook` | Low level keyboard hook (WH_KEYBOARD_LL). Keeps a tiny buffer only after a `:` that follows a space or line start. Swallows arrows, Enter, Tab and Esc while the popup is open. Text is never stored. |
| `ContextGuard` | Skips password fields, ignored apps and elevated windows. |
| `CaretLocator` | Finds the text cursor: GetGUIThreadInfo, then UI Automation, then MSAA. Falls back to the mouse position. |
| `EmojiIndex` | Built in emoji data with Discord, Slack and GitHub shortcodes plus keyword tags. In memory search ranked by exact, prefix, tag, then usage and recency. |
| `SuggestionPopup` | WPF window that never takes focus (WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW). Rounded, shadow, follows Windows light or dark mode. |
| `TextInjector` | Backspaces over `:query`, types the emoji with SendInput unicode. Clipboard paste fallback restores the clipboard after. |
| `TrayHost` | Tray icon: on/off, pause 1 hour, settings, quit. |
| `SettingsWindow` | Start with Windows, ignored apps, result count, popup size. |
| `Storage` | JSON in %AppData%\EmojiAnywhere: settings and emoji usage counts only. |

No networking code anywhere in the app.

## Build order

1. Core loop working in Notepad: type `:fire`, pick, insert.
2. CaretLocator and all smart rules (trigger rules, password fields, ignored apps).
3. Tray, settings, theming, polish.
