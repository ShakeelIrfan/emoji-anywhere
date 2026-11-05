<!-- TODO before launch: remove the status note below, add demo.gif to docs/, uncomment the demo line, set up the GitHub Actions release build, and re-check every claim here against the real app. -->

> **🚧 Status: in development.** Not ready to download yet. Star the repo to get notified when v1 drops.

<div align="center">

# Emoji Anywhere

### Type `:fire:` → get 🔥. In every app on Windows.

Discord style emoji autocomplete for the apps you type in. Built to work anywhere you can type ([see where it's tested](docs/TESTING.md)).
Free, open source, and it never touches the internet.

<!-- ![Emoji Anywhere demo](docs/demo.gif) -->

[**Download for Windows**](../../releases/latest) · [How it works](#how-it-works) · [Privacy](#privacy-you-can-check)

![Windows 10 and 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![License MIT](https://img.shields.io/badge/license-MIT-green)
![No internet](https://img.shields.io/badge/internet-never-black)

</div>

---

## You already know the shortcodes. Now they work everywhere.

On Discord you type `:skull:` and it just works. Then you switch to WhatsApp, Word or your code editor, and you're back to opening a menu and scrolling for the right face.

Emoji Anywhere fixes that. Same shortcodes, every app, no clicking. If you've used Rocket on Mac, this is that idea for Windows.

```
:fire:    →  🔥   (converts the moment you close it)
:skull:   →  💀
:sob:     →  😭
:sad      →  😢 😭 😓   (not sure of the name? search by meaning)
```

## How it works

1. Type `:` and at least 2 letters, anywhere you can type.
2. A small popup appears right at your text cursor.
3. Keep typing to narrow it down, use the arrow keys to pick, press **Enter** or **Tab**.
4. Your `:query` turns into the emoji. That's it.

Press **Esc** to close the popup and keep typing normally.

## Why it feels invisible

- **Gets out of your way.** It only triggers when `:` comes after a space or at the start of a line, so `10:30`, `https://` and `C:\` never open it.
- **Never steals focus.** The popup shows up, you keep typing in your app.
- **Learns you.** The emojis you use most rank first.
- **Knows Discord, Slack and GitHub shortcodes**, plus keywords and tags.
- **Every emoji up to Emoji 17.0**, including all skin tones (`:thumbsup_tone3:` → 👍🏽).
- **Stays out of password fields**, always.
- **Ignored apps list** for games, terminals or anything you want left alone.
- **Your clipboard is safe.** If an app needs the emoji pasted in, your clipboard gets put back right after.
- **Looks native.** Follows your Windows light or dark mode, Windows 11 style.

## Privacy you can check

An app that sees your keyboard has to earn your trust. Here's how this one does it:

| Promise | How you can check |
|---|---|
| No internet, ever | The code has no networking at all. Search the repo for `HttpClient`, `WebRequest` or `Socket` and you'll find nothing. Or block it in your firewall and watch it keep working. |
| Never saves what you type | Typed text only lives in memory while the popup is open, then it's gone. The only file it writes is your settings and a count of which emojis you picked. |
| No accounts, no tracking | There's nothing to sign up for and nothing to send data to. |
| The download matches the code | Release builds are made by GitHub Actions straight from this public code, with a signed record of which commit they came from. Check any download with `gh attestation verify EmojiAnywhere.exe --repo ShakeelIrfan/emoji-anywhere`. |

Everything is open source under the MIT license. Read every line.

## Which download?

- **EmojiAnywhere.exe** (about 3 MB) needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Windows offers to install it the first time if you don't have it.
- **EmojiAnywhere-standalone.exe** is bigger but runs with nothing else installed.

## Tray menu

Emoji Anywhere lives in your system tray:

- Turn it on or off
- Pause for 1 hour
- Settings: start with Windows, ignored apps, how many results to show, popup size
- Quit

## FAQ

**Why does it need keyboard access?**
To notice when you type `:`. That's the whole trick. It ignores everything else you type and never stores it.

**Will it work in games?**
It's meant to stay out of games. Add any game to the ignored apps list and it won't react there at all.

**Does it work in apps running as administrator?**
Windows doesn't let normal apps read keys typed into admin windows, so not unless you run Emoji Anywhere as admin too.

**Is it really free?**
Yes. No ads, no premium version, no catch.

## Build from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 or 11.

```
git clone https://github.com/ShakeelIrfan/emoji-anywhere.git
cd emoji-anywhere
dotnet build src/EmojiAnywhere
```

## Tests

`tests/SearchTest` checks the emoji list and search on every push. `tests/LiveTest` runs the real app and types into windows it opens itself (a text box, Edge, Chrome, VS Code). See [tests/README.md](tests/README.md) and the latest [results](docs/TESTING.md).

## Contributing

Found a missing shortcode or a bug? Open an issue or a pull request.

---

<div align="center">

Made by **Shakeel** · If it saved you a scroll, drop a ⭐

</div>
