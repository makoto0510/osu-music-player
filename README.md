# osu! music player

[English](README.md) | [日本語](README.ja.md)

[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.x-9b59b6)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)](#system-requirements)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](#credits--disclaimer)

A feature-rich desktop music player that directly loads and plays your local **osu!stable** and **osu!lazer** song libraries without needing to export or duplicate files.  
Beyond audio playback, it faithfully renders beatmap **background videos**, **hitsounds**, and **storyboards**, and features an autoplay-style **difficulty preview**.

> [!NOTE]
> **Read-Only Safety Design:**  
> This player accesses your osu! directories strictly in **read-only** mode. It will never alter, overwrite, or corrupt your beatmaps or database files. Playlists, user configurations, and cached data are saved entirely independently in the player's own application data folder.

---

## Table of Contents

- [Features](#features)
- [System Requirements](#system-requirements)
- [Download & Installation](#download--installation)
- [Quick Start](#quick-start)
- [Feature Guide](#feature-guide)
  - [Library Integration & Search](#library-integration--search)
  - [UI Modes & Themes](#ui-modes--themes)
  - [Visuals (Video, Storyboard & Difficulty Preview)](#visuals-video-storyboard--difficulty-preview)
  - [Hitsound Synchronization](#hitsound-synchronization)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Integrations](#integrations)
  - [Web Remote & OBS Overlay](#web-remote--obs-overlay)
  - [Headless Server](#headless-server)
  - [Discord Rich Presence & osu! API](#discord-rich-presence--osu-api)
- [Data & Configuration Storage](#data--configuration-storage)
- [Building & Development](#building--development)
- [Troubleshooting](#troubleshooting)
- [Credits & Disclaimer](#credits--disclaimer)
  - [Inspiration](#inspiration)
  - [Disclaimer](#disclaimer)
  - [Open Source & Third-Party Libraries](#open-source--third-party-libraries)

---

## Features

- **Seamless Library Integration**  
  Automatically detects local stable and lazer libraries, intelligently deduplicating overlapping tracks. Supports game collections (`collection.db` and Realm collections).
- **High-Fidelity Audio Engine**  
  Powered by the BASS audio engine. Includes queue management, shuffle, repeat modes, playback speed and pitch adjustments (DT / NC / HT / DC mods), and a 10-band graphic equalizer with presets.
- **Beatmap Visuals & Effects**  
  - Background video playback synchronized to audio via libVLC, with speed-mod tracking.
  - Storyboard rendering powered by SkiaSharp (supports additive blending, rotations, animations, and hitsound triggers).
  - Synchronized hitsound playback (configurable between beatmap-specific samples and skin/default soundfonts).
  - Minimalistic autoplay difficulty preview across all 4 game modes (osu!, taiko, catch, mania).
- **Customizable User Interface**  
  Choose between a modern **Studio Mode** layout and a traditional media player **Classic Mode**. Includes 6 curated color themes and support for custom `#RRGGBB` accent colors.
- **Rich Integrations**  
  Browser-based **Web Remote** for smartphone/tablet control with audio streaming ("Play here"), transparent **OBS Studio Overlay** for live streaming, **Discord Rich Presence**, and automatic metadata enrichment (genre/language) via **osu! API v2**.

---

## System Requirements

| OS | Target Architecture | Supported osu! Library | Background Video Support |
| :--- | :--- | :--- | :--- |
| **Windows** | x64 | osu!stable / osu!lazer | Automatic via bundled/NuGet libVLC |
| **macOS** | Apple Silicon (arm64) / Intel (x64) | osu!lazer only | libVLC bundled during build |
| **Linux** | x64 / arm64 | osu!lazer only | Requires system libVLC (`apt install libvlc-dev` etc.) |

- A local osu! installation with song files is required.
- Native binaries for BASS and BASS_FX are pre-bundled for all supported platforms.

---

## Download & Installation

### Using Prebuilt Releases (Recommended)

1. Download the archive matching your OS and architecture from [GitHub Releases](https://github.com/makoto0510/osu-music-player/releases):
   - Windows: `OsuMusicPlayer-win-x64.zip`
   - macOS: `OsuMusicPlayer-osx-arm64.zip` (Apple Silicon) / `OsuMusicPlayer-osx-x64.zip` (Intel)
   - Linux: `OsuMusicPlayer-linux-x64.tar.gz` / `OsuMusicPlayer-linux-arm64.tar.gz`
2. Extract the archive into a folder of your choice.
3. Launch the application:
   - **Windows:** Run `OsuMusicPlayer.App.exe`.
   - **macOS / Linux:** Grant executable permissions and run `OsuMusicPlayer.App`:
     ```sh
     chmod +x OsuMusicPlayer.App
     ./OsuMusicPlayer.App
     ```

> [!TIP]
> **Self-Contained Builds:** If you download a self-contained release package, no external .NET runtime installation is necessary.  
> If using framework-dependent builds, the **.NET 8 Desktop Runtime** (and ASP.NET Core Runtime) must be installed on your machine.

---

## Quick Start

1. **Launch and Auto-Detection**  
   Upon opening, the player automatically scans standard directory paths to detect your osu!stable / osu!lazer installations and loads your library.
2. **Adding Directories Manually**  
   If your osu! installation is in a custom path or songs are not detected, open **Sources** in the toolbar to add your directories:
   - **osu!stable (Windows only):** Select the folder containing `osu!.db` and the `Songs` directory.
   - **osu!lazer:** Select the folder containing `client.realm` and the `files` directory.
3. **Play Music**  
   Double-click any track in the list to start playback immediately.
4. **Reloading Library**  
   After downloading or importing new beatmaps in osu!, click **Reload** in the header to refresh your library.

---

## Feature Guide

### Library Integration & Search

When both stable and lazer are detected, identical songs are merged automatically by audio hash and metadata into a clean, unified track entry.

#### Search Query Cheat Sheet

The search bar (`Ctrl+F` or `/`) supports extensive filtering syntax:

| Query Example | Description |
| :--- | :--- |
| `artist:xi` | Filter by artist name |
| `title:freedom` | Filter by song title |
| `mapper:sotarks` | Filter by beatmap creator (mapper) |
| `mode:mania` | Filter by game mode (`osu` / `taiko` / `catch` / `mania`) |
| `source:lazer` | Filter by library source (`stable` / `lazer`) |
| `bpm:180-240` | Filter by BPM range |
| `stars:>6.5` | Filter by star difficulty rating |
| `length:<3:00` | Filter by track duration (e.g. under 3 minutes) |
| `genre:anime` | Filter by genre (after fetching osu! API metadata) |
| `language:japanese` | Filter by language (after fetching osu! API metadata) |
| `-remix` | Exclude terms matching "remix" |
| `camellia|t+pazolite` | OR search (matches either term) |
| `"sweet love"` | Exact phrase matching with spaces |

#### Organization & Playlists
- **Smart Playlists:** Save your current search criteria as dynamic playlists that automatically update as new songs match the query.
- **Recommendations:** Suggests unplayed songs based on your listening history (tags, artists, mappers, BPM).
- **Export to collection.db:** Export your active track view into a stable-compatible `collection.db` file (direct overwrite into osu! directory is blocked for safety).

---

### UI Modes & Themes

Switch between two layout styles under **Settings → Appearance → Interface / UI**:

- **Studio Mode (Default):** Maximizes library browsing space with quick-access slide-out utility panels (Settings, Sources, Equalizer, Browse, Playlists).
- **Classic Mode:** Traditional 3-pane layout featuring a left navigation sidebar, central track table, right Now Playing sidebar, and bottom playback bar.

#### Themes & Accent Colors
- **Presets:** osu! Pink, Lazer Purple, Midnight Blue, Forest, OLED Black, and Daylight.
- **Custom Accent:** Specify any `#RRGGBB` hex color code for personalized UI accents.

---

### Visuals (Video, Storyboard & Difficulty Preview)

Toggle visual elements on the fly from the track details pane:

- **Background Video:** Plays beatmap background videos in millisecond sync with BASS audio, seamlessly adapting to playback speed mods (DT/HT).
- **Storyboard:** Decodes `.osb` and difficulty-specific `.osu` files to render complex storyboards using SkiaSharp.
- **Pop-out & Fullscreen (`Ctrl+P` / `F11`):** Detach video and storyboard playback into a separate window for multi-monitor setups or theater mode.
- **Difficulty Preview (`Ctrl+Shift+P`):** Visualizes note placements and rhythmic patterns across all 4 modes in a lightweight autoplay overlay.

---

### Hitsound Synchronization

Enabling **Hitsounds** plays hit sounds (normal, whistle, clap, finish) in exact rhythm with the song playback:

- **Beatmap Soundfont:** Prioritizes custom samples bundled within the beatmap folder.
- **Skin Soundfont:** Uses your configured skin or default sound fonts for consistent feedback.
- Customizable device latency offset can be calibrated in Settings.

---

## Keyboard Shortcuts

Global shortcuts enable rapid, keyboard-driven navigation (viewable anytime in **Settings → Shortcuts**):

| Shortcut | Action |
| :--- | :--- |
| `Space` / `Enter` | Play / Pause (or play selected track) |
| `Ctrl + ←` / `Ctrl + →` | Previous track / Next track |
| `Shift + ←` / `Shift + →` | Seek 5 seconds backward / forward |
| `Ctrl + ↑` / `Ctrl + ↓` | Volume up / down |
| `Ctrl + F` or `/` | Focus search bar |
| `Ctrl + D` | Toggle favourite |
| `Ctrl + E` | Add to playback queue |
| `Ctrl + H` | Temporarily hide selected track |
| `Ctrl + S` / `Ctrl + R` | Toggle Shuffle / Repeat mode |
| `Ctrl + Q` | Toggle Queue / Now Playing pane |
| `Ctrl + T` | Toggle Theater Mode |
| `Ctrl + P` / `F11` | Pop-out visuals window / Toggle Fullscreen |
| `Ctrl + Shift + P` | Toggle Difficulty Preview |
| `Ctrl + ,` | Open Settings |
| `Esc` | Clear search / Exit fullscreen or pop-out window |

*Note: Single-key shortcuts like `Space`, `Enter`, and `/` are disabled during text input. Hardware media keys are also supported.*

---

## Integrations

### Web Remote & OBS Overlay

Enabling the internal server (default port: `5150`) exposes web-based controls and streaming assets:

| Path | Description & Use Case |
| :--- | :--- |
| `http://<IP>:5150/` | **Web Remote UI:** Control playback, browse tracks, and stream audio directly to mobile devices via "Play here". |
| `http://localhost:5150/overlay` | **OBS Browser Source:** Transparent now-playing overlay designed for stream layouts (recommended size: 600 × 120). |
| `/api/tracks` | Query track list with pagination and search filter support. |
| `/api/tracks/{id}/audio` | Live HTTP audio stream (supports HTTP Range requests). |
| `/api/tracks/{id}/background` | Fetch beatmap background image. |
| `/api/state` | Current playback state (track, seek position, volume, queue). |

> [!CAUTION]
> The internal server operates without authentication. Only use it within trusted local networks (LAN). Do NOT expose port 5150 directly to the public internet.

---

### Headless Server

For dedicated music streaming on headless setups (e.g. Raspberry Pi or Linux home servers), use the `OsuMusicPlayer.ServerHost` binary:

```sh
dotnet run --project ./src/OsuMusicPlayer.ServerHost -- --port 5150 --lazer /path/to/osu-lazer-data
```
- `--local-only`: Restricts connections to `localhost` only.
- `--stable <path>`: Specifies an osu!stable directory (Windows only).

---

### Discord Rich Presence & osu! API

- **Discord Rich Presence:** Broadcasts song title, artist, difficulty, and playback progress to your Discord profile activity status.
- **osu! API v2 Integration:** Provide your osu! OAuth credentials (Client ID / Secret) under Settings to automatically fetch and cache official genre and language metadata.

---

## Data & Configuration Storage

All player settings and cache databases are stored in standard OS application data folders:

- **Settings File:** `%AppData%\OsuMusicPlayer\settings.json` (or OS-equivalent `ApplicationData` path on macOS/Linux).
  - Stores configured source paths, volume, themes, playlists, and history.
- **Online Metadata Cache:** `online-metadata.json`
  - Stores cached genre/language information retrieved via osu! API.

---

## Building & Development

### Prerequisites
- **.NET 8 SDK** (C# 12)
- Target operating system environment

### Clone and Run

```sh
# Restore dependencies
dotnet restore ./OsuMusicPlayer.sln --configfile ./NuGet.Config

# Run the desktop application
dotnet run --project ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj
```

### Publishing Standalone Releases (`publish`)

Build self-contained binaries for target architectures:

```sh
# Windows x64
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r win-x64 --self-contained true -o ./artifacts/publish/win-x64

# macOS Intel
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r osx-x64 --self-contained true -o ./artifacts/publish/osx-x64

# macOS Apple Silicon
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r osx-arm64 --self-contained true -o ./artifacts/publish/osx-arm64

# Linux x64
dotnet publish ./src/OsuMusicPlayer.App/OsuMusicPlayer.App.csproj -c Release -r linux-x64 --self-contained true -o ./artifacts/publish/linux-x64
```

### Running Tests

```sh
# Unit tests (xUnit)
dotnet test ./OsuMusicPlayer.sln

# Integration test harness with local osu! data
dotnet run --project ./tools/OsuMusicPlayer.IntegrationHarness -- all
```

#### Project Structure

```
src/
 ├── OsuMusicPlayer.Core/       # Domain models, osu!.db / Realm parsers, deduplication logic
 ├── OsuMusicPlayer.Audio/      # BASS / BASS_FX wrappers, equalizer, hitsound synthesis
 ├── OsuMusicPlayer.Server/     # Kestrel server, streaming API, Web Remote, OBS overlay
 ├── OsuMusicPlayer.ServerHost/ # Headless server host
 └── OsuMusicPlayer.App/        # Avalonia UI desktop application, settings and themes
tests/                          # Unit tests
tools/                          # Integration testing harnesses
third_party/                    # Native BASS binaries and license documents
```

---

## Troubleshooting

| Issue | Cause & Recommended Action |
| :--- | :--- |
| **Songs do not appear in the library** | Check **Sources** in the toolbar. Ensure osu!stable points to a folder containing `osu!.db` and `Songs`, or lazer points to `client.realm` and `files`. Then click **Reload**. |
| **Audio engine fails to initialize** | Verify that you downloaded the build matching your OS and architecture, and ensure all native libraries (such as `bass.dll`) are present in the application folder. |
| **Background video does not play** | Verify that the beatmap contains a video file and that **Video** is enabled in the details pane. On Linux, ensure `libvlc-dev` is installed on your system. |
| **macOS blocks application launch** | Caused by macOS Gatekeeper for unsigned binaries. Allow execution under System Settings → Privacy & Security. |
| **Cannot connect to Web Remote** | Verify that the server is enabled in Settings, port `5150` is allowed through your firewall, and **Allow LAN** is checked. |

---

## Credits & Disclaimer

### Inspiration
This project is heavily inspired by [Osu-Player](https://github.com/Milkitic/Osu-Player) by Milkitic. Special thanks for the pioneering concept and brilliant ideas!

### Disclaimer
- This is an unofficial, community-driven fan project and is not affiliated with or endorsed by ppy Pty Ltd or the osu! team.
- "osu!" is a registered trademark of ppy Pty Ltd.

### Open Source & Third-Party Libraries
This project is built possible thanks to these open-source libraries and components:

- **UI Framework:** [Avalonia UI](https://avaloniaui.net/)
- **Audio Engine:** [ManagedBass](https://github.com/fiload/ManagedBass) / [Un4seen BASS & BASS_FX](https://www.un4seen.com/)  
  *(For BASS non-commercial licensing terms, please refer to [third_party/README.md](third_party/README.md))*
- **osu! Parsing:** [OsuParsers](https://github.com/DuskyVanilla/OsuParsers)
- **Database Engine:** [Realm .NET SDK](https://github.com/realm/realm-dotnet)
- **Video Playback:** [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) / [VideoLAN VLC](https://www.videolan.org/)
- **Storyboard Engine:** [ReOsuStoryboardPlayer](https://github.com/MikiraSora/ReOsuStoryboardPlayer)
- **Hitsound Reference:** [KeyASIO.Net](https://github.com/Milkitic/KeyASIO.Net)
