<p align="center">
  <img src="kitsune7den-logo.png" width="280" alt="Kitsune7Den Logo" />
</p>

<h1 align="center">Kitsune7Den</h1>
<p align="center"><strong>7 Days to Die Server Manager</strong></p>
<p align="center">A standalone Windows desktop app for managing your 7D2D dedicated server.<br/>No web stack. No browser. Just a simple exe.</p>

<p align="center">
  <a href="https://www.nexusmods.com/7daystodie/mods/10067"><img src="https://img.shields.io/badge/Download-Nexus_Mods-da8e35?logo=nexusmods&logoColor=white" alt="Download on Nexus Mods" /></a>
  <a href="https://github.com/Kitsune-Den/Kitsune7Den/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Kitsune-Den/Kitsune7Den/ci.yml?branch=main&label=build&logo=github" alt="Build Status" /></a>
  <a href="https://github.com/Kitsune-Den/Kitsune7Den/releases/latest"><img src="https://img.shields.io/github/v/release/Kitsune-Den/Kitsune7Den?label=release&color=e94560" alt="Latest Release" /></a>
  <a href="https://github.com/Kitsune-Den/Kitsune7Den/releases"><img src="https://img.shields.io/github/downloads/Kitsune-Den/Kitsune7Den/total?label=downloads&color=4ecca3" alt="Total Downloads" /></a>
  <a href="https://github.com/Kitsune-Den/Kitsune7Den/blob/main/LICENSE"><img src="https://img.shields.io/github/license/Kitsune-Den/Kitsune7Den?color=blue" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/platform-windows-0078D4?logo=windows" alt="Platform: Windows" />
  <a href="https://ko-fi.com/T6T57VRO7"><img src="https://img.shields.io/badge/support-ko--fi-FF5E5B?logo=kofi&logoColor=white" alt="Support on Ko-fi" /></a>
</p>

---

## Features

- **Dashboard** -- Start/stop/restart server, connect telnet, view LAN/public IPs with click-to-copy
- **Console** -- Live server log tailing + telnet command input with history
- **Players** -- Player cards with stats, admin badges, kick/ban, give/remove admin via serveradmin.xml
- **Configuration** -- Grouped form editor with 90+ properties, smart dropdowns, Raw XML toggle
- **Mods** -- List installed mods, enable/disable, delete, install from zip
- **Backups** -- Manual + scheduled backups, restore with automatic safety backup, auto-prune
- **Logs** -- Browse all server log files with dropdown selector and text filter
- **Settings** -- SteamCMD install/update, auto-update on start, telnet config
- **Themes** -- 4 live-swappable themes (Kitsune, Midnight, Forest, Accessible/colorblind-friendly)
- **Config Protection** -- Automatically backs up serverconfig.xml before Steam updates, restores after

## Screenshots

### Dashboard
Start/stop/restart the server, view uptime, and copy LAN/public addresses in one click.

![Dashboard](docs/screenshots/dashboard.jpg)

### Players
Live player cards with stats, admin badges, and one-click kick/ban/admin toggle — reads and writes `serveradmin.xml` directly.

![Players](docs/screenshots/players.jpg)

### Configuration
Grouped form editor for every property in `serverconfig.xml`. Smart dropdowns, auto-discovered world list, day/night calculator, raw XML toggle.

![Configuration](docs/screenshots/configuration.jpg)

### Mods
Card-based mod browser reading `ModInfo.xml`. Enable, disable, delete, or install directly from a zip.

![Mods](docs/screenshots/mods.jpg)

### Backups
Manual or scheduled backups of your save. Restore with an automatic safety backup. Auto-prunes old backups.

![Backups](docs/screenshots/backups.jpg)

## Quick Start

1. Download `Kitsune7Den.exe` from [Releases](https://github.com/Kitsune-Den/Kitsune7Den/releases)
2. Run it — *if Windows shows a SmartScreen warning, see [INSTALL.md](INSTALL.md) for why and how to proceed*
3. Browse to your `7DaysToDieServer.exe`
4. Hit Start

## Building from Source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
dotnet run --project src/Kitsune7Den
```

To publish a single-file exe:

```bash
dotnet publish src/Kitsune7Den -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish
```

## Tech Stack

- .NET 8 / WPF / MVVM (CommunityToolkit.Mvvm)
- Telnet client for server commands + player data
- XDocument for serverconfig.xml + ModInfo.xml + serveradmin.xml parsing
- SteamCMD integration for server install/update

## Support the project

Kitsune7Den is free and open source. If it saves you time or makes your server life easier, you can buy me a coffee. No pressure, only gratitude.

<p align="center">
  <a href="https://ko-fi.com/T6T57VRO7"><img src="https://storage.ko-fi.com/cdn/kofi2.png?v=3" alt="Support on Ko-fi" height="40" /></a>
</p>

## License

MIT
