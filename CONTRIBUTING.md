# Contributing

Thanks for helping. Bug reports, missing shortcodes and small fixes are all welcome.

## Reporting a problem

Open an issue with the "Something doesn't work" template. The most useful detail is which app you were typing in,
because the popup position and the way emoji get inserted differ between apps.

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10 or 11.

```
dotnet build src/EmojiAnywhere -c Release
dotnet run --project tests/SearchTest -c Release
```

`tests/LiveTest` drives the real app with real keystrokes, but only in windows it opens itself, and it stops if
anything else takes focus. Quit any running copy of Emoji Anywhere first. See [tests/README.md](tests/README.md).

## Ground rules

- No network code. The app must keep working with no internet connection, and must never store typed text.
- Keep the keyboard hook fast. Anything slow goes off the hook thread (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)).
- Claims in the README must match [docs/TESTING.md](docs/TESTING.md).
