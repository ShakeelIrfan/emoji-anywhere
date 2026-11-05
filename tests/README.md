# Tests

## SearchTest

Checks the emoji list and search ranking. No UI, no keyboard. Runs on GitHub on every push.

```
dotnet run --project tests/SearchTest
```

## LiveTest

Runs the real app and types into real windows to check the whole flow: popup, picking, inserting.

**It types with your keyboard, so don't touch the mouse or keyboard while it runs.** It only types
into windows it opens itself, and it stops the moment any other window gets focus.
Browser and VS Code modes use throwaway profiles in the test folder, never your own.

```
dotnet build src/EmojiAnywhere
dotnet run --project tests/LiveTest -- <path to EmojiAnywhere.exe> <empty test folder> [mode]
```

| Mode | What it tests |
|---|---|
| `type` (default) | A plain Windows text box: codes, Enter, Tab, arrows, Esc, Backspace, skin tones, things that must not trigger (`10:30`, `a:fire:`), password fields, popup placement, clicking the popup, speed, memory |
| `paste` | Paste mode inserts the emoji and puts your clipboard back |
| `browser` | Microsoft Edge |
| `chrome` | Google Chrome |
| `vscode` | VS Code (Electron, like Discord and WhatsApp desktop) |
