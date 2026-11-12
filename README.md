# RimTouch – Windows 1.7 continuation

A Windows touch continuation of Tatsuki Tanaka’s RimTouch. Provides touch selection, UI repair, pinch zoom, panning, world-map controls and input cleanup for RimWorld 1.7.

Upstream: https://github.com/iLtanaka/RimTouch. Original author: Tatsuki Tanaka. Windows continuation by Sweet Briana and Cinder. GPL-3.0-or-later is retained for the continuation. The source snapshot is based on local commit c7bfa3adc60bc23ee5ea101f23320db934ef22b1, including main-menu cleanup guards. Metadata retains candidate wording; compilation is not certification for all touchscreens, display scaling, twist gestures, Linux or arbitrary mod lists.

- Package ID: `cinder.rimtouch17.private`
- Code license: GPL-3.0-or-later. See LICENSE.
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3827698857

## Test releases (download here)

Test builds are shipped as **GitHub Releases** in this fork — they do
not expire, unlike the old file links. Always download from the
Releases page:

**https://github.com/Stella-sea/rimworld-rimtouch/releases**

- Latest test build: **v0.2.0-test12** (2026-11-11, commit `dc20615`) —
  flat settings page, dual-axis mode, jitter fix, vanilla north-lock
  fix, debug-window scroll fix. See `CHANGELOG.md` for the full list
  and the 7210 test checklist.
- Each release asset is named `RimTouch-v<version>.zip` and contains a
  single `RimTouch/` folder: extract it straight into
  `RimWorld/Mods/` (disable the Workshop RimTouch first, enable
  Harmony separately, enable only one RimTouch). The SHA256 in the
  release notes identifies the exact build.
- Version numbers follow `VERSIONING.md`: the mod version (e.g.
  `v0.2.0-test12`) is independent of the game version (RimWorld 1.7);
  `testN` builds are prereleases, a plain `v0.2.0` is stable and only
  ships after approval on the 7210.

## Source release

This is the selected production source and metadata snapshot, with game, Harmony and provider assemblies omitted. Original workspaces, installed mods, active lists and player saves are unchanged. This GitHub publication does not update Steam Workshop. See BUILD-STATUS.md for fresh compilation evidence and its limits.

## Test builds

Test builds are published as GitHub pre-releases on this fork (the Steam Workshop is not updated from GitHub):

https://github.com/Stella-sea/rimworld-rimtouch/releases

Each test build is tagged `v0.1.0-test.<N>` (a SemVer pre-release; the number continues the test1, test2, … series) and ships as `RimTouch-fix-test<N>.zip`. See [RELEASES.md](RELEASES.md) for the versioning and release scheme.

## Build for 1.7

Use PowerShell 7 and an installed .NET SDK. Supply your licensed game and matching Harmony assembly:

```powershell
./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
```

The portable helper compiles only Source/ into .build/1.7, without downloading dependencies, starting a game, copying a runtime or changing installed mods. Archived IDE project files may retain legacy references; the 1.7 helper is the supported build path for this snapshot. Legacy source, where included, is separate and has not been newly tested. Assets have separate terms in ASSETS.md.
