# Changelog

All notable changes to Kitsune7Den are documented here.

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
