# Aperture Portal

A lightweight desktop game launcher for Windows that brings your library together in one place — sync installed games from Steam, Epic Games, and GOG, add anything else by hand, and browse it all from a couch-friendly Console Mode built for a gamepad.

![Desktop mode](docs/screenshots/desktop-mode.jpg)

## Features

- **Steam sync** — pulls in games already installed on your PC, or your whole owned library (including Family Sharing) via browser sign-in or a Steam Web API key. Tracks real install/download progress by reading Steam's own manifests.
- **Epic Games & GOG sync** — local, automatic detection of installed games, no login required.
- **Manual add** — for anything else (Battle.net, Xbox/Game Pass, or a plain .exe), point it at the executable and tag which platform it's from.
- **Console Mode** — a fullscreen, gamepad-navigable view of your library for when you're on the couch.
- **In-game overlay** — pause, resume, or exit a running game with a controller (Back+Start), without alt-tabbing out.
- **Cover art** — auto-fetched from [SteamGridDB](https://www.steamgriddb.com/) (needs a free API key), or set manually.

|  |  |
|---|---|
| ![Console Mode](docs/screenshots/console-mode.jpg) | ![In-game overlay](docs/screenshots/overlay.jpg) |

## Install

Download the latest installer from [Releases](../../releases). It installs per-user (no admin rights needed) and adds a Start Menu shortcut.

Requires Windows 10/11 (x64).

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
```

To produce a self-contained release build (what the installer packages):

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

### Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). After publishing (above):

```
ISCC.exe installer\ApertureOS.iss
```

The compiled installer lands in `installer-output\`.

## Notes

- Steam login via the embedded browser never sees your password — it's a real Steam sign-in page, and only the resulting session is used to read your library.
- The "Log in via Steam" flow depends on the WebView2 Runtime, which ships with Windows 11 and most up-to-date Windows 10 installs. If it doesn't work, install it from [Microsoft's WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/).
- Epic/GOG-synced games and manually-added games launch directly rather than through their platform's own client, since ApertureOS has no install/download tracking for those platforms. A small number of anti-cheat-protected Epic titles may need to be re-added pointed at the Epic Games Launcher instead.
