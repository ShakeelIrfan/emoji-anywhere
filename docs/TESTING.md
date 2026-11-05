# Where it has been tested

Live tested on Windows 11 on 2026-11-05 with `tests/LiveTest`.

| App | Result |
|---|---|
| Plain Windows text box (WinForms) | 18 of 18 checks pass, popup at the text cursor |
| Paste mode | 2 of 2 pass, clipboard restored |
| Microsoft Edge | 7 of 7 pass, popup at the text cursor |
| Google Chrome | 7 of 7 pass |
| VS Code | 7 of 7 pass, popup at the text cursor |

Measured in the plain text box test:

- Popup shows 3 to 6 ms after the key that opens it.
- About 8 MB of RAM in use while idle, 13 seconds after the last popup.
- No CPU use while idle.

## Known issues

- VS Code: when typing in the middle of a long line, the popup can appear at the right edge of the
  window instead of under the cursor (found while recording on 2026-11-06). Inserting the emoji still works.

## Not tested yet

Windows 11 Notepad, Word, Discord, Slack, WhatsApp desktop, games (chat in windowed, borderless
and exclusive fullscreen), apps running as administrator,
non US keyboard layouts, more than one monitor with different scaling.
