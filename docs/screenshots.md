# Updating the README images

The images live in `docs/images/` and must keep these names:

| File | What it shows |
| --- | --- |
| `flyout.png` | The flyout with 2–4 apps playing sound. |
| `menu.png` | The right-click tray menu. |
| `editor.png` | Configure profiles, with a profile selected. |

## Steps

1. Run `run.cmd` once so the Release build exists.
2. Run `demo.cmd`. Volumy restarts with sample profiles: **Alex, Sam, Jordan, Riley**.
   They have no devices or app rules, so clicking them doesn't change your audio.
   Your real profiles stay in `%APPDATA%\Volumy\profiles.json`; the demo uses `demo-profiles.json`.
3. Play something in 2–3 apps (Spotify, a browser tab, Discord) so the mixer has rows.
4. Screenshots: `Win + Shift + S` → **Window** or **Rectangle** mode, then save the PNG.
   A dark, plain wallpaper makes the glass look best.
5. GIF: [ScreenToGif](https://www.screentogif.com/) (free). Record just the flyout area at 15–20 fps,
   then **Save as → GIF**. Keep it under ~5 MB so GitHub shows it quickly.
6. Run `run.cmd` (or open Volumy normally) to go back to your own profiles.
