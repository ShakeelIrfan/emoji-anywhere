# Security

Emoji Anywhere reads your keyboard, so security reports are taken seriously.

## Reporting a problem

Please do not open a public issue for security problems. Use GitHub's private
[security advisory form](https://github.com/ShakeelIrfan/emoji-anywhere/security/advisories/new) instead.
You should get a reply within a few days.

## What the app does and does not do

- It has no networking code and never connects to the internet.
- It keeps only the short `:query` you are typing, in memory, while the popup is open.
- It skips password fields and any app you add to the ignored list.
- It writes only `settings.json` and `usage.json` in `%AppData%\EmojiAnywhere`.
- It runs as your normal user. It never asks for administrator rights.
- Release files are built by GitHub Actions from this repository, with build provenance attestations.

## Website

The website has no cookies and no analytics. The optional feedback form stores only the chosen answer,
an optional note, the app version and the date. See [the privacy policy](https://emoji-anywhere.vercel.app/privacy).
