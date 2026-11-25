# RimTouch – Windows 1.7 continuation

A Windows touch continuation of Tatsuki Tanaka’s RimTouch. Provides touch selection, UI repair, pinch zoom, panning, world-map controls and input cleanup for RimWorld 1.7.

Upstream: https://github.com/iLtanaka/RimTouch. Original author: Tatsuki Tanaka. Windows continuation by Sweet Briana and Cinder. GPL-3.0-or-later is retained for the continuation. The original source snapshot was based on local commit c7bfa3adc60bc23ee5ea101f23320db934ef22b1; subsequent fixes are recorded in CHANGELOG.md. Compilation and reported hardware acceptance do not certify all touchscreens, display scaling, Linux or arbitrary mod lists.

- Package ID: `cinder.rimtouch17.private`
- Code license: GPL-3.0-or-later. See LICENSE.
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3827698857

## Source release

This repository contains the continuation source and metadata, with game, Harmony and provider assemblies omitted. GitHub publication does not update Steam Workshop. See BUILD-STATUS.md for dated compilation evidence and its limits.

## Current release

**0.2.0** is the stable release version for RimWorld 1.7. Download `RimTouch-v0.2.0.zip` from GitHub Releases and extract its single `RimTouch/` folder into `RimWorld/Mods/`:

https://github.com/Stella-sea/rimworld-rimtouch/releases

This release includes the world-camera, slider, UI ownership and map-tap dispatch fixes accepted in hardware testing after test15. Active integration runs on `dev` and promotes to `main` via a merge-commit PR; stable tags are cut on `main`. GitHub releases do not update Steam Workshop. See [RELEASES.md](RELEASES.md), [CONTRIBUTING.md](CONTRIBUTING.md), and [CHANGELOG.md](CHANGELOG.md).

## Build for 1.7

Use PowerShell 7 and an installed .NET SDK. Supply your licensed game and matching Harmony assembly:

```powershell
./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
```

The portable helper compiles Source/ into .build/1.7 and refreshes one complete local test mod at `.build/local/RimTouch/` (DLL, About, Languages, license and credits files). Copy that folder to test; each successful build replaces its contents, while a failed compilation leaves the previous test mod intact. Local testing does not generate ZIPs or versioned test folders. The helper does not download dependencies, start the game, copy a runtime or change installed mods. Archived IDE project files may retain legacy references; the 1.7 helper is the supported build path for this snapshot. Legacy source, where included, is separate and has not been newly tested. Assets have separate terms in ASSETS.md.

## Optional input diagnostics

Enable **Temporarily log touch diagnostics to Debug log** in RimTouch settings, reproduce the issue, then use **Copy compact touch diagnostics** (Chinese: **复制精简触控诊断**). This copies JSON without per-entry game stack traces; the normal Debug log remains available. No launch arguments, restart or separate output file are needed. The temporary checkbox is not saved and capture stops after 1024 records; enable it again for a new capture. Existing command-line/file capture remains optional. See [the capture guide](docs/input-diagnostics.md) for fields and limits.
