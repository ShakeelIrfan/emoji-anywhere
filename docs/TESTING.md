# Where it has been tested

Live tested on Windows 11 with `tests/LiveTest`, last run on 2026-11-06.

| App | Result |
|---|---|
| Plain Windows text box (WinForms) | 18 of 18 checks pass, popup at the text cursor |
| Paste mode | 2 of 2 pass, clipboard restored |
| Search (`tests/SearchTest`) | 18 of 18 pass, search under 2 ms |
| Tray panel (`tests/FlyoutTest`) | 16 of 16 pass |
| Microsoft Edge | 7 of 7 pass, popup at the text cursor |
| Google Chrome | 7 of 7 pass |
| VS Code | 9 of 9 pass, popup follows the text cursor, including in the middle of a long line |

Measured in the plain text box test:

- Popup shows 3 to 6 ms after the key that opens it.
- About 8 MB of RAM in use while idle, 35 seconds after the last popup (the app trims its memory 30 seconds after the popup closes).
- No CPU use while idle.

## Known issues

- None open. Fixed on 2026-11-06: in VS Code the popup could land away from the cursor in the middle of a
  line, because VS Code's UI Automation text box reports a placeholder spot. The app now reads the
  accessibility caret first, which VS Code and other Chromium apps keep up to date.

## Apps with their own shortcodes

Discord and Slack already show their own `:emoji` popup, so Emoji Anywhere stays quiet in them by default (tray panel: "Stay quiet in Discord and Slack"). Found 2026-11-07 when both popups showed at once in Discord.

## Not tested yet

Windows 11 Notepad, Word, Discord, Slack, WhatsApp desktop, games (chat in windowed, borderless
and exclusive fullscreen), apps running as administrator,
non US keyboard layouts, more than one monitor with different scaling.
