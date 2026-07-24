# Aperture Portal

A desktop game launcher for Windows built around one idea: Windows never gave you a real console style rest mode. Every other launcher makes you close your game, or just minimizes it and calls it a day. Aperture Portal actually suspends the game and puts the PC to sleep, then wakes both back up exactly where you left off. PlayStation rest mode, but on your PC.

On top of that it's a full game launcher too. It pulls your library together from Steam, Epic, GOG, emulators, and anything else you point it at, then gives you a couch friendly, gamepad only Console Mode to browse it all from.

![Desktop mode](docs/screenshots/desktop-mode.jpg)

## Features

**Sleep without losing your place.** Hit Sleep from the in-game overlay and Aperture Portal suspends the game's process (not just alt-tabs away from it) and puts the whole PC to sleep. Wake the PC back up and resume, and the game picks up exactly where it was, not from a checkpoint or an autosave.

**In-game overlay.** Pause, resume, exit, or sleep a running game with a controller (Back+Start) or the keyboard (Ctrl+P), without ever alt-tabbing out.

**Build your library from wherever your games actually are.** The "+ Add Games" hub covers every source in one place:
* **Steam:** installed games are detected automatically with no login needed, and real install/download progress comes straight from Steam's own manifests. For your full owned library (including Family Sharing), sign in through an embedded browser or use a Steam Web API key.
* **Epic Games & GOG:** local, automatic detection of anything already installed, no login required.
* **Emulators:** tell Aperture Portal where an emulator lives and where its ROMs are, and it can scan that folder for you, whether your ROMs sit loose in one place or each has its own subfolder buried a level or two deep.
* **Installed folders:** point it at any folder of PC games that aren't on a launcher at all, and it finds the real executables for you.
* **Manual add:** for anything else (Battle.net, Xbox/Game Pass, or a plain .exe), point it at the file and pick which platform it's from.

Every scan gives you a checklist to review before anything gets added, so nothing gets pulled in by mistake.

**Categories.** Your library organizes itself by console and platform automatically as you add games, and you can rename any of those to whatever you like without breaking anything. You can also build your own custom categories and choose exactly which games go in each one.

**Console Mode.** A fullscreen, gamepad navigable view of your library for when you're on the couch. Built for Xbox compatible (XInput) controllers, with arrow keys, Enter, and Escape as a keyboard fallback.

**Cover art.** Auto-fetched from [SteamGridDB](https://www.steamgriddb.com/) (needs a free API key), or picked manually per game.

**Startup options.** Launch with Windows, and/or skip straight to Console Mode instead of the normal window.

**Update notifications.** Aperture Portal checks GitHub for new releases on startup and shows a quiet "Update available" button in the title bar when one exists. Nothing downloads or installs automatically, it just points you at the release page.

|  |  |
|---|---|
| ![Console Mode](docs/screenshots/console-mode.jpg) | ![In-game overlay](docs/screenshots/overlay.jpg) |

## Hotkeys

**Keyboard**

| Key | Where | Does |
|---|---|---|
| Ctrl+P | While a game is running | Opens the in-game overlay (pause/resume/exit/sleep). |
| Arrow keys | Console Mode | Move the selection around the grid. |
| Enter | Console Mode | Launch the selected game. |
| Escape | Console Mode | Go back / exit Console Mode. |

**Controller**

| Button | Where | Does |
|---|---|---|
| Back + Start | While a game is running | Opens the in-game overlay. Same as Ctrl+P. |
| D-pad / left stick | Main window & Console Mode | Move the selection. |
| A | Main window & Console Mode | Launch / activate the selected game. |
| B | Console Mode | Go back. |
| LB / RB | Console Mode | Switch between filter tabs (Installed, Recently Played, All, Not Installed). |
| X / Y | Console Mode | Step through your category chips. |

## Install

Download the latest installer from [Releases](../../releases). It's a per-user install, no admin rights and no UAC prompt required, and you get a Start Menu shortcut when it's done.

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

* Steam login via the embedded browser never sees your password. It's a real Steam sign-in page, and only the resulting session is used to read your library.
* The "Log in via Steam" flow depends on the WebView2 Runtime, which ships with Windows 11 and most up to date Windows 10 installs. If it doesn't work, install it from [Microsoft's WebView2 page](https://developer.microsoft.com/microsoft-edge/webview2/).
* Epic, GOG, emulator, and manually added games launch directly rather than through their platform's own client, since Aperture Portal has no install/download tracking for those. A small number of anti-cheat protected Epic titles may need to be re-added pointed at the Epic Games Launcher instead.
* Console and platform categories are figured out from your games automatically, so you never have to build them by hand. Custom categories are entirely yours to create and manage.
* Your library, settings, and cover art cache all live in `%APPDATA%\ApertureOS`, never in this repo or the installer. A fresh install always starts empty.
* Works well as the local front end for a [Sunshine](https://github.com/LizardByte/Sunshine)/Moonlight streaming setup, since Console Mode is fully controller-driven and needs no keyboard or mouse.

## Feedback

This is a solo project, still rough around the edges, and I'd rather improve it alongside people who want the same thing than guess in the dark. If something's broken or missing, [open an issue](../../issues). I'm reading them.
