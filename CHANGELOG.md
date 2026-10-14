# Changelog

All notable changes to Kitsune7Den are documented here.

## [Unreleased]

### Added
- **Extra launch arguments** (Settings → Server Launch), for anything you'd otherwise add to `startdedicated.bat`. They go just before `-dedicated`, where the .bat's extra arguments go, and a live preview shows the full command.
  - **Custom config file:** `-configfile=yourconfig.xml` swaps the config file everywhere. The server, the Config page, telnet, backups and the Steam-update protection all use it.
  - **Protected arguments:** the ones Kitsune7Den relies on (`-logfile`, `-quit`, `-batchmode`, `-nographics`, `-dedicated`) are ignored, with a note.

### Infrastructure
- **Releases are code-signed automatically.** `scripts/publish-release.ps1` now signs the exe with Azure Artifact Signing (via `az login`, or a service principal from environment variables), verifies the signature, and only then writes the SHA-256. If signing fails it stops instead of producing an unsigned release; `-SkipSign` builds unsigned on purpose. It no longer deletes everything in `publish\`, only its own build output.
- **INSTALL.md** now covers checking the signature. v1.1.0 and v1.1.1 were signed by hand.

## [1.1.1] - 2026-10-14

### Fixed
- **Telnet said "Connected" when the server had rejected the password**, so commands quietly did nothing and the Players page always showed 0 (reported on Nexus). Kitsune7Den now waits for the server to confirm the login. If it doesn't, the status bar says why (Wrong password / Password needed / Off in config) and the Console explains how to fix it.
- **Telnet used stale port and password values.** They were copied once when you browsed to the exe, so changing them in serverconfig.xml later broke telnet. Kitsune7Den now reads them from serverconfig.xml every time it connects, falls back to Settings, and updates Settings to whatever worked.
- **Automatic reconnect stops on a rejected password** instead of retrying, which could trip the server's TelnetFailedLoginLimit block.
- **Player coordinates read as 0** on Windows set to a comma-decimal language (e.g. German, French).

## [1.1.0] - 2026-10-14

### Added
- **7 Days to Die 3.x support (tested against 3.3).** 3.0 moved the gameplay rules (difficulty, XP, loot, zombie speed, blood moons, day length, and more) out of `serverconfig.xml` and into one `SandboxCode`. Kitsune7Den now knows that:
  - **Game version detection** from the server's own log (`Version: V 3.3.0 (b18)`), a pinned SteamCMD branch, or the 3.x install files. It's shown at the top of the Configuration page.
  - **Sandbox editor.** All 165 Sandbox options, grouped like the game's own Sandbox Options tabs, plus the built-in presets (Scavenger to Insane, and the Official set). It reads and writes `SandboxCode` exactly as the game's copy-code button does, with an option table for each of 3.0, 3.1, 3.2 and 3.3, so the letters mean the right thing on your server's version.
  - **"Update for 3.x"** for configs from before 3.0. It comments out (never deletes) the 29 properties 3.x ignores, adds `SandboxCode`, `Region` and `Language`, and takes a timestamped backup first. Running it again changes nothing.
  - On a 3.x server, the ignored properties move into a **Legacy** section with a note, instead of looking like they still do something. The Day/Night calculator only shows where it still applies.
  - New fields: **Region**, **Language**, **SandboxCode**.
- **Simplified Chinese (简体中文).** The whole app is translated, including every config field and option. Config and Sandbox labels use the game's own official Chinese wording from its `Localization.csv`, so they match what players see in-game. Kitsune7Den follows the Windows display language on first run, and you can switch at any time in **Settings → Appearance → Language / 语言**. The switch is live, with no restart. Translated config fields still show the real property name, e.g. `玩家杀戮 (PlayerKillingMode)`.
- RWG world discovery also scans `UserDataFolder/GeneratedWorlds` when `serverconfig.xml` overrides it.
- **Automatic restarts** ([#4](https://github.com/Kitsune-Den/Kitsune7Den/issues/4)), in a new card on the Dashboard:
  - **Crash restart.** If the server exits without Kitsune7Den asking it to, it starts again 15 seconds later. It stops after 3 crashes in 15 minutes, so a broken mod or config can't loop forever. Using the Stop button, or typing `shutdown` in the Console, doesn't count as a crash.
  - **Scheduled restarts.** Restart every day at set times (`04:00, 16:00`) or after N hours of uptime.
    - Players get in-game `say` warnings beforehand. The default is at 10, 5 and 1 minutes; both the times and the messages can be changed.
    - At restart time the world is saved, then the server shuts down gracefully and starts again.
    - The Dashboard shows when the next restart is. You can cancel a countdown, or trigger "Restart with warnings" by hand.
  - Telnet now reconnects with retries after a start or restart (for up to about 5 minutes), instead of one fixed 15-second wait that 3.x servers often outlast.

### Fixed
- **Player Killing Mode** labels for 1 and 2 were swapped. The game's own definition is 1 = Kill Allies Only, 2 = Kill Strangers Only.
- **Land Claim Decay Mode** labels were wrong. They're now 0 = Slow (Linear), 1 = Fast (Exponential), 2 = None.
- **Kick, Ban, Give Admin and Remove Admin** on player cards did nothing, because they acted on a "selected" player that was never set. They now act on the card you click.
- **Restore and Delete** on backup cards could act on a different backup than the card you clicked.
- Leaving the Configuration page and coming back silently threw away unsaved edits. It now only reloads when there's nothing unsaved.
- Descriptions that didn't match the game: Reserved Slots, Admin Slots, Telnet Password (blank = localhost only), World Gen Size (officially 6144 to 10240), and others now follow the 3.3 `serverconfig.xml` comments.

## [1.0.3] - 2026-04-14

### Added
- **Support the Project card** in Settings with a Ko-fi button so users can optionally throw a coffee my way if Kitsune7Den's been useful to them.
- Matching Ko-fi badge at the top of the README and a Support section near the bottom.

### Infrastructure (shipped in a prior PR, now baked into this release)
- **62 xUnit tests** across 6 suites covering the self-updater, Day/Night math, mod manager, config parser, admin file, and backup service.
- **GitHub Actions CI** builds and runs tests on every push and PR, with coverage reporting.
- **README badges** for build status, latest release, total downloads, license, .NET 8, and platform.
- **INSTALL.md** explaining the SHA-256 verification and the Windows SmartScreen warning for unsigned open source builds.
- **scripts/publish-release.ps1** builds the Release exe and generates a sha256sum-format checksum alongside it.

## [1.0.2] - 2026-04-14

### Added
- **Game World auto-discovery** — Game World dropdown now scans `Data/Worlds` and `%AppData%/7DaysToDie/GeneratedWorlds` and populates with every installed/generated world (built-in Navezgane, Empty, Playtesting, Pregen maps, and user RWG worlds). Still editable so you can type a custom world name.
- **Day/Night Calculator** — friendly "Day Minutes / Night Minutes" helper in the Gameplay config section that auto-calculates the raw `DayNightLength` and `DayLightLength` values.
- **Auto-detect telnet settings** — when you browse to `7DaysToDieServer.exe`, the telnet port and password are read from `serverconfig.xml` automatically.
- **Keyboard shortcuts** — Ctrl+S saves the config page, F5 refreshes player/mod/backup lists.
- **Window size/position persistence** — Kitsune7Den remembers where you had the window last time.
- **LICENSE + CHANGELOG** — MIT license and this changelog.

### Changed
- **Sidebar logo** — now full-width at the top of the sidebar, branding baked into the image (removed the redundant "Kitsune7Den / 7D2D Server Manager" text label).
- **Config editor Save button** — disabled when there are no unsaved changes.

### Fixed
- First-run empty states when no server is configured.

## [1.0.1] - 2026-04-07

### Added
- Settings polish: password field styling, clickable GitHub link, Terms of Use section, unified version display.

## [1.0.0] - 2026-04-06

Initial release.

### Features
- **Dashboard** — Start/stop/restart server, connect telnet, view LAN/public IPs with click-to-copy.
- **Console** — Live server log tailing + telnet command input with history.
- **Players** — Player cards with stats, admin badges, kick/ban, give/remove admin via `serveradmin.xml`.
- **Configuration** — Grouped form editor with 90+ properties, smart dropdowns, Raw XML toggle.
- **Mods** — List installed mods, enable/disable, delete, install from zip.
- **Backups** — Manual + scheduled backups, restore with automatic safety backup, auto-prune.
- **Logs** — Browse all server log files with dropdown selector and text filter.
- **Settings** — SteamCMD install/update, auto-update on start, telnet config.
- **Themes** — 4 live-swappable themes (Kitsune, Midnight, Forest, Accessible/colorblind-friendly).
- **Config Protection** — Automatically backs up `serverconfig.xml` before Steam updates, restores after.
