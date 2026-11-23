# Innovation Easy Minecraft Launcher (IEML)

[繁體中文](README.md) | **English**

[![Version](https://img.shields.io/github/v/release/wayjehet/InnovationEasyMinecraftLauncher?label=version&color=00FFFF)](https://github.com/wayjehet/InnovationEasyMinecraftLauncher/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/wayjehet/InnovationEasyMinecraftLauncher/build.yml?label=build)](https://github.com/wayjehet/InnovationEasyMinecraftLauncher/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4)
![Platform](https://img.shields.io/badge/platform-Windows-0078D6)
![Status](https://img.shields.io/badge/status-Beta-orange)
![AI co-authored](https://img.shields.io/badge/AI-co--authored-8A2BE2)

A Minecraft: Java Edition launcher written in **.NET 8 + WPF**, targeting Windows.

> ⚠️ **May be unstable**
> Many features were written without a build environment and have not been fully tested. Expect bugs. Bug reports are welcome.

> 🤖 **AI co-authorship notice**
> Most of the code, documentation and UI previews in this project were written with the help of AI (Claude), then integrated and tested by the author. See [AI co-authorship notes](#ai-co-authorship-notes).

> 🌐 The UI is currently in Traditional Chinese only.

## Table of contents

- [Features](#features)
- [UI tour](#ui-tour)
- [System requirements](#system-requirements)
- [Install and run](#install-and-run)
- [Build from source](#build-from-source)
- [Usage](#usage)
- [Security design](#security-design)
- [Project structure](#project-structure)
- [Known limitations](#known-limitations)
- [AI co-authorship notes](#ai-co-authorship-notes)
- [Reporting issues](#reporting-issues)
- [License and credits](#license-and-credits)

## Features

### Instances and launching
- **Multiple instances**: each instance has its own Minecraft version, loader, loader version, Java path, max memory and extra JVM arguments.
- **Version list**: show releases only, or include snapshots and old versions. New instances are auto-named from version and loader, and can be renamed.
- **Loaders**: Fabric, Quilt, Forge, NeoForge (including the legacy 1.20.1 naming).
  - Pick a loader version in the instance settings ("Auto" = latest stable, or type one manually).
  - Changing the loader type on an instance that already has mods shows a warning first. If the Minecraft version changes and the loader version is no longer in the new list, it resets to "Auto".
- **Java management**: detects local Java automatically; if no suitable version is found, downloads one from Adoptium into `java\`. You can also point to a Java path manually.
- **Shared cache**: instances share libraries / assets to save disk space and download time.
- **Game log**: live game output and launch errors, with access tokens automatically masked.

### Accounts
- **Offline accounts**: UUID generated as `MD5("OfflinePlayer:" + name)`, identical to vanilla; can be renamed or deleted.
- **Microsoft (premium) login**: multiple accounts can be saved, each with its own sign-in cache, and sign in silently on the next launch.
- Accounts can be bound to instances; the main screen can launch the same instance with different accounts.
- Premium accounts show their skin avatar (fetched from mc-heads.net) on the main screen.

### Content installation (mods / shaders / resource packs / modpacks)
- **Modrinth**: mods, shaders, resource packs, modpacks (`.mrpack`).
  - Filters: category, loader (Iris / OptiFine / Canvas for shaders), side (client / server), Minecraft version.
  - Sorting: relevance, downloads, follows, newest, recently updated; load more with paging.
- **Pick a specific version**: click a search result to open the project detail page, which lists every version (version number, release type, supported Minecraft versions and loaders, date, downloads) and the changelog. You can show only versions matching the current instance, auto-install required dependencies, and tick optional ones. Works for both Modrinth and CurseForge (for modpacks, pick a specific version to create the instance).
- **CurseForge**: mods, shaders, resource packs, modpacks (you supply your own API key).
- **Local modpack import**: CurseForge `.zip`, Modrinth `.mrpack`.
- **Dependency handling**: required dependencies are installed automatically; release versions are preferred.
- **Download verification**: a file is only installed if an official hash is available (SHA-1, MD5 only as a fallback). A mismatch deletes the file and fails.
- **Installed content management**: lists mods / shaders / resource packs; tick = enable / disable (adds `.disabled` to the file name, nothing is deleted); delete; open folder; check for and update to newer releases ("Update all" is available; updates are verified before the old file is removed, and the disabled state is preserved).
- **Drag-and-drop import**: drag a resource pack / shader `.zip` or a mod `.jar` into the "Mods & Content" view.
  - Validates the zip; resource packs need `pack.mcmeta` at the root, shaders need `shaders/` at the root (an extra wrapping folder is rejected with an explanation).
  - Never overwrites same-name files (adds `(1)`, `(2)`…); writes to `.part` first and renames afterwards, so a failure never leaves a half-written file.
  - Handy for files whose authors disallow third-party launcher downloads and that you downloaded in a browser yourself.
- **Files whose authors disallow third-party downloads**: the launcher does **not** bypass the restriction. They are listed in `manual-downloads.txt` in the instance folder; download them manually from the CurseForge website.

### Maps
- Import a zip / folder, download from a direct zip URL, or search the CurseForge maps category (API key required).
- Automatically finds the shallowest folder containing `level.dat` and places it in `saves`; protected against Zip Slip; never overwrites same-name maps (adds `(2)`).
- Back up to `backups\<name>-timestamp.zip`, delete, open folder.
- Entry points: the "Maps" tab on the download page, or "More → Map management".

### Multiplayer and servers
- **Terracotta (P2P)**: the default P2P option. Create a room, join with a room code, see status and members, copy the room code / direct address. Room codes look like `U/XXXX-XXXX-XXXX-XXXX` and are compatible with HMCL and PCL-CE. The launcher only downloads the **unmodified** official executable and runs it as a separate process, controlling it through its local HTTP API.
- **EasyTier rooms (advanced)**: builds a virtual LAN via [EasyTier](https://github.com/EasyTier/EasyTier) so you can play with friends (administrator rights are requested to create the virtual network adapter).
- **Server lookup**: Server List Ping, SRV resolution, live status, favorites.
- **Public server search**: browse and search public servers (data from newminecraftservers.net), with paging, live status, favorites and copy-address.

### Seed map
- Uses [Cubiomes](https://github.com/Cubitect/cubiomes) to look up structure coordinates, check the biome at the center, and copy coordinates.
- The repo ships a prebuilt x64 `native\ieml_cubiomes.dll`. Everything else works without this DLL.

### Music player
- Built-in simple player: play / pause / previous / next, volume, and a selectable music folder (scanned recursively).
- Supports `.mp3`, `.wav`, `.flac`, `.m4a`, `.wma`.

### Appearance and personalization
- Dark theme (`#0F0F0F` background, `#1C1C1C` panels, cyan `#00FFFF` accent).
- **Theme colors**: in Settings, adjust seven parts with R / G / B sliders (accent, window background, panel / card background, borders and dividers, primary text, icons / secondary text, text on accent). Changes apply live while dragging; "Restore defaults" is available, and the colors persist across launches (stored in `theme.json`).
- **Asset skinning (custom images)**: the window background, logo, avatar frame, panels, quick-launch cards, dialogs, menus, all kinds of buttons (with hover / pressed / disabled state images), text boxes, dropdowns, tabs and progress bars can be replaced with your own images. Use "Choose image" in Settings, or drop PNGs named after each part into the assets folder; **changes apply instantly, no restart needed**. Supports 9-slice stretching and pixel-art (nearest-neighbor) scaling, and skins can be exported / imported as a zip pack to share. Missing images fall back to the default style (the part list and recommended sizes are in `src/IEML.App/Assets/README.txt`).
- **Quick-launch cards**: cards in the middle column can be dragged anywhere on the canvas and positions are saved; the card you press comes to the front; each card has "Launch" and "Settings" buttons; cards that end up off-screen after shrinking the window are pulled back automatically; Settings has "Reset quick-card positions".

### Updates and packaging
- **Auto-update**: checks the latest GitHub release at startup (at most once every 12 hours); it **always asks for your consent** before downloading and never updates silently. Choices: "Update", "Later", "Skip this version". Auto-check can be turned off in Settings, or you can press "Check for updates" manually.
  - Verifies SHA-256, with optional ECDSA P-256 signatures; protected against Zip Slip; automatically rolls back if swapping files fails; the `.old` backup is removed on the next launch.
  - See [`docs/UPDATE.md`](docs/UPDATE.md).
- **Download manager** (`Services/Download/`, being integrated): verify `.part` then rename, HTTP Range pause / resume, automatic retry, global concurrency limit. For the steps callers need to take, see [`docs/DOWNLOAD-INTEGRATION.md`](docs/DOWNLOAD-INTEGRATION.md).
- **GitHub Actions**: pushing a `v*` tag builds a self-contained single-file package, zips it and publishes a Release with a `.sha256` (plus a `.sig` when a signing key is configured).

## UI tour

The main window has three columns (980×640, resizable), roughly **left 311 : middle 551 : right 117**.

| Area | Content |
| --- | --- |
| **Left** | IEML logo, avatar (click to open account management), instance name / version / loader, account dropdown, progress bar and status text, **Launch**, [Instance settings] [Instance picker] |
| **Middle** | Quick-launch cards (draggable, positions saved), "+ Add card"; shows a hint when there are no cards |
| **Right** | **Settings**, **Download**, **P2P**, **More**, **Music**; clicking one switches the middle column to that page, and "← Back" returns home |

Page contents:

- **Settings**: account management, CurseForge API key, theme colors, assets folder and asset list, reset card positions, version info and Beta notice, check for updates / auto-update toggle, GitHub project page.
- **Download**: tabs "Games" (version list + create instance), "Content" (Modrinth / CurseForge search, version picking and install), "Maps"; an "Import modpack" button sits in the page header. Manage installed mods / shaders / resource packs in "Instance settings".
- **P2P**: Terracotta, EasyTier rooms.
- **More**: server lookup, game log, seed map, map management, account management.
- **Music**: playback controls, volume, music folder.

## System requirements

- Windows 10 / 11
- Running the published self-contained build: **no** separate .NET install needed
- Building from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Using the default (non-self-contained) output of `publish.ps1`: .NET 8 Desktop Runtime

## Install and run

1. Download the latest `InnovationEasyMinecraftLauncher-vX.Y.Z-NET8-SelfContained.zip` from this repo's **Releases**.
2. Extract it into **your own folder** (not `Program Files`, otherwise auto-update cannot swap files).
3. Run `InnovationEasyMinecraftLauncher.exe`.

Windows SmartScreen may warn about a freshly downloaded exe (the project has no code-signing certificate). This is unrelated to the update mechanism.

## Build from source

```powershell
git clone https://github.com/wayjehet/InnovationEasyMinecraftLauncher
cd InnovationEasyMinecraftLauncher
dotnet build
dotnet run --project src/IEML.App
```

Publish as a single exe:

```powershell
./publish.ps1                 # requires the .NET 8 Desktop Runtime on the user's machine
./publish.ps1 -SelfContained  # bundles the runtime, larger file
```

Output goes to `dist\`. If `native\ieml_cubiomes.dll` exists it is copied to the output folder automatically.

### Publishing a new release

1. Edit `<Version>` (numeric) and `<InformationalVersion>` (display name) in `src/IEML.App/IEML.App.csproj`.
2. Push a tag matching the version, e.g. `v1.2.2`.
3. GitHub Actions builds and publishes the Release.

> The updater only looks at the leading numbers of the version: `1.2.1EF` and `1.2.1EG` are treated as the same version and **will not prompt each other to update**. For older versions to receive an update, the number must increase.

### Seed map DLL (optional)

The repo ships an x64 `ieml_cubiomes.dll`. To update it or change the bitness, rebuild following [`native/README.md`](native/README.md).

## Usage

### CurseForge API key

CurseForge search and modpack installation need an API key (importing Modrinth `.mrpack` files does not):

1. Apply at [console.curseforge.com](https://console.curseforge.com).
2. Paste it into the Settings page or the CurseForge page and save.

The key is stored only on your computer, encrypted with your Windows account (DPAPI), and is never written into the code. Key priority: **user-entered > built-in at packaging time** (the built-in key in the repo is always an empty string).

### Signing in to a Microsoft account

"Settings → Open account management → Add premium account", then follow the on-screen instructions. After signing in, the next launch signs in silently; if you have to sign in every time, please attach the "Game log" to your report.

### Adding files you downloaded yourself to an instance

Select the instance on the main screen, open the "Mods & Content" view, and drag in a `.jar` (mod) or `.zip` (resource pack / shader). A `.zip` is treated as a resource pack by default; it is treated as a shader only when shaders is the selected category.

### Enabling update signatures (optional, one-time)

Recommended: it protects against a tampered GitHub account or Release. See [`docs/UPDATE.md`](docs/UPDATE.md) for the steps. **Never put the private key in the repo, and never paste it to anyone (including AI).**

### Data locations

All data is stored in `%AppData%\InnovationEasyLauncher`:

| Path | Content |
| --- | --- |
| `accounts.json` | Account list (refresh tokens encrypted with DPAPI) |
| `ms\*.dat` | Microsoft sign-in cache (DPAPI-encrypted) |
| `instances\<id>\` | Per-instance settings, `mods`, `shaderpacks`, `resourcepacks`, `saves`, `manual-downloads.txt` |
| `shared\` | Shared libraries / assets cache |
| `tools\` | Loader installers, EasyTier, Terracotta (`tools\terracotta\`) and other tools |
| `java\` | Automatically downloaded Java |
| `backups\` | Map backups |
| `Assets\` | Custom asset PNGs (take priority over `Assets\` next to the exe) |
| `theme.json` | Theme colors |
| `update.json` | Auto-update preferences (auto-check on/off, last check time, skipped version) |
| `curseforge.json` | Your CurseForge key (encrypted) |

Also:
- `%LocalAppData%\InnovationEasyLauncher\tmp\`: temporary files during sign-in (wiped right after use).
- `%LocalAppData%\InnovationEasyLauncher\update\`: update downloads and `apply-update.log`.

## Security design

- **Sign-in credentials**: the Microsoft sign-in cache (including Microsoft / Xbox / Minecraft tokens) is encrypted with **Windows DPAPI (current user)** and stored as `ms\*.dat`. Copying it to a USB drive, another PC or another Windows user cannot decrypt it. It is only decrypted into a temporary file during sign-in, then re-encrypted, overwritten with zeros and deleted right after; leftover temp files from a previous crash are cleaned up at the next launch. Plaintext `ms\*.json` files left by older versions are migrated and deleted automatically.
- **Purpose isolation**: the sign-in cache, the CurseForge key and the account refresh token use different DPAPI entropy; old formats are readable and upgraded automatically. Decrypted bytes are wiped from memory after use.
- **Limits**: DPAPI cannot stop malware running under the same Windows user, nor someone using your computer while you are signed in. Do not sign in on untrusted computers, and lock the screen when you leave. SSD wear leveling also means zero-overwriting cannot be guaranteed to erase data.
- When the game is launched, the access token is still passed to the game process (as with every launcher); the launcher's game log masks it.
- If you suspect a token leak, sign out of all devices or change your password on your Microsoft account security page, and the old tokens become invalid.
- **Download verification**: CurseForge / Modrinth files must have an official hash, and domains are restricted to https CurseForge / Modrinth / GitHub / GitLab (including the final URL after redirects). A hash mismatch deletes the file and fails; a missing hash refuses the install. A hash only proves "the file was not tampered with", not "the mod itself is safe", so install mods from trusted sources only.
- **Modpack import**: path traversal (Zip Slip) aborts immediately; executables such as `.exe`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.msi`, `.lnk` inside `overrides/` are never installed; download domains in a `.mrpack` are restricted to the Modrinth / GitHub / GitLab allowed by the spec; `.jar` files bundled in a modpack but not in its download list are warned about in the game log (many legitimate modpacks bundle jars, so they are not blocked outright).
- **Launching external programs**: Terracotta and EasyTier command-line arguments use `ArgumentList` rather than string concatenation; the PowerShell path used when running as administrator is checked for unsafe characters.
- **Auto-update**: downloads only from the official repo's Releases; domain allowlist; SHA-256 verification (optional signature); no "silent update".

## Project structure

```
src/
  IEML.Core/   No UI: data models, Contracts (service interfaces), Services (implementations)
  IEML.App/    WPF UI (windows, Theme.xaml theme, Assets)
native/        C wrapper for Cubiomes, prebuilt DLL and build notes
third_party/   Bundled third-party binaries (official Terracotta, unmodified) and their license notes
docs/          UI preview (ui-preview, open in a browser), download manager integration notes, auto-update notes
.github/       GitHub Actions (build and release)
HANDOFF.md     Development progress and a list of things still to test (Traditional Chinese)
```

Tech: .NET 8, WPF, CommunityToolkit.Mvvm, CmlLib.Core 4.x, CmlLib.Core.Auth.Microsoft 3.x, NAudio.

## Known limitations

- The project is still early-stage and many features have not been tested (see the to-test list in `HANDOFF.md`).
- Some code was written by AI without a build environment, so the first build may need compile fixes (CmlLib API names are the most likely to break).
- The download manager is not yet wired into every download caller; vanilla files downloaded by CmlLib and the libraries of Forge / NeoForge installers do not go through it.
- Old files without an official hash are refused ("no official checksum to verify").
- The release package ships the official Terracotta v0.4.2 (x64, unmodified, in `third_party\terracotta\`), copied to `tools\terracotta\` on first use. On ARM machines, or if the bundled files were deleted, it is downloaded from the official Release instead (`terracotta-<version>-windows-<arch>-pkg.tar.gz`); if upstream renames the asset and it cannot be found, manually place `terracotta*.exe` (together with the DLL next to it) into that folder.
- Until update signing is enabled, only SHA-256 is verified.
- A few components (check boxes, scroll bars, sliders, list items, toast notifications) cannot be skinned with images yet.
- A few hard-coded colors in the code (some input backgrounds, gray text) do not follow the theme colors; the sliders themselves still use the default WPF style.
- Modrinth has no maps category, and sites such as Planet Minecraft have no public API, so maps must be downloaded in a browser and then imported.
- Windows only for now.

## AI co-authorship notes

This project is co-authored by the author and an AI assistant (Anthropic Claude). Please be aware before using or contributing:

- **AI makes mistakes**: AI-generated code may contain bugs, outdated API usage or security issues. Many parts were written from memory and are **not yet compiled or tested**; progress and items to verify are in `HANDOFF.md`.
- **Review it yourself**: for important features (Microsoft sign-in, token encryption, file download and extraction, auto-update, launching EasyTier with administrator rights), read the source before relying on them day to day.
- **External service details may be wrong**: EasyTier parameters, the Terracotta API, public-server API fields, Modrinth / CurseForge response formats and so on may be misremembered by the AI; treat the official documentation as authoritative.
- **Disclose in contributions**: if your Pull Request makes heavy use of AI-generated content, say so in the description and confirm that you have actually built and tested it.
- **License and provenance**: AI-generated content may resemble existing code. If you spot a possible infringement, please open an Issue and we will handle it promptly.
- **No data collection**: AI collaboration happens only during development. The launcher itself never sends your accounts, tokens or logs to any AI service.

## Reporting issues

Please report them in this repo's Issues and include:

- Launcher version ("About" in Settings) and Windows version
- Minecraft version and loader used
- The contents of the "Game log" (tokens are masked automatically, but please double-check before posting)

## License and credits

Released under the [MIT License](LICENSE).

**Terracotta © burningtnt (AGPL-3.0-or-later, with exception clause)**: the release package ships the unmodified official executable as a bundled binary (`third_party\terracotta\`, with a `NOTICE.txt` giving source, SHA-256 and license notes) and only downloads it from the official Release when needed; the launcher interacts with it as a separate process over an HTTP API, without linking or modifying it. Both uses fall under the upstream AGPL exception, and a verbatim copy of the upstream `LICENSE` is included in that folder; its copyright notice is displayed prominently in the UI as the license requires.

Thanks to CmlLib.Core, Fabric, Quilt, Forge, NeoForge, Modrinth, CurseForge, EasyTier, Terracotta, Cubiomes, Adoptium, NAudio, mc-heads.net and other projects and services, as well as the AI tools that helped with development.

This project is not affiliated with Mojang Studios or Microsoft and is not an official product of either. Minecraft is a trademark of Mojang Studios.
