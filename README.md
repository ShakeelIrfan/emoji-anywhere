# Emoji Anywhere

Discord style emoji autocomplete for every app on Windows.

Type `:fire` in Notepad, VS Code, Chrome, Word, WhatsApp or anywhere else you type, pick from the popup, and get 🔥.

> **Status:** work in progress. Not ready to download yet.

## How it will work

- Type `:` followed by 2 or more letters. A small popup shows matching emojis at your text cursor.
- Arrow keys to move, Enter or Tab to insert, Esc to close.
- Type a full shortcode like `:fire:` and it converts instantly.
- Uses the shortcodes you already know from Discord, Slack and GitHub, plus keywords (`:sad` finds 😢 😭 😞).
- Learns what you use most and ranks it first.

## Privacy

Privacy is the main point of this app.

- No internet connection. The app contains no networking code.
- No accounts, no tracking, no analytics.
- It never saves what you type. It only remembers which emojis you picked, so it can rank them.
- Open source, so you can check all of this yourself.

## Build from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 or 11.

```
dotnet build src/EmojiAnywhere
```

## License

MIT. See [LICENSE](LICENSE).

Made by Shakeel.
