# RimTouch – Windows 1.7 continuation

A Windows touch continuation of Tatsuki Tanaka’s RimTouch. Provides touch selection, UI repair, pinch zoom, panning, world-map controls and input cleanup for RimWorld 1.7.

Upstream: https://github.com/iLtanaka/RimTouch. Original author: Tatsuki Tanaka. Windows continuation by Sweet Briana and Cinder. GPL-3.0-or-later is retained for the continuation. The source snapshot is based on local commit c7bfa3adc60bc23ee5ea101f23320db934ef22b1, including main-menu cleanup guards. Metadata retains candidate wording; compilation is not certification for all touchscreens, display scaling, twist gestures, Linux or arbitrary mod lists.

- Package ID: `cinder.rimtouch17.private`
- Code license: GPL-3.0-or-later. See LICENSE.
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3827698857

## Source release

This is the selected production source and metadata snapshot, with game, Harmony and provider assemblies omitted. Original workspaces, installed mods, active lists and player saves are unchanged. This GitHub publication does not update Steam Workshop. See BUILD-STATUS.md for fresh compilation evidence and its limits.

## Test builds

Test builds are published as GitHub pre-releases on this fork (the Steam Workshop is not updated from GitHub):

https://github.com/Stella-sea/rimworld-rimtouch/releases

Each test build is tagged `v0.1.0-test.<N>` (a SemVer pre-release; the number continues the test1, test2, … series) and ships as `RimTouch-fix-test<N>.zip` containing a single `RimTouch/` folder — extract it straight into `RimWorld/Mods/`. Latest: **test15** (2026-11-24, `ButtonInvisibleDraggable` multi-pass tap dedup, exact HUD & window hit-testing, nested `ScrollView` priority, unified gesture lifecycle & turntable sensitivity fix). Active integration runs on `dev` and promotes to `main` via PR after tablet acceptance; see [RELEASES.md](RELEASES.md), [CONTRIBUTING.md](CONTRIBUTING.md), and [CHANGELOG.md](CHANGELOG.md).

## Build for 1.7

Use PowerShell 7 and an installed .NET SDK. Supply your licensed game and matching Harmony assembly:

```powershell
./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
```

The portable helper compiles Source/ into .build/1.7 and refreshes one complete local test mod at `.build/local/RimTouch/` (DLL, About, Languages, and license files). Copy that folder to test; each successful build replaces its contents, while a failed compilation leaves the previous test mod intact. Local testing does not generate ZIPs or versioned test folders. The helper does not download dependencies, start the game, copy a runtime or change installed mods. Archived IDE project files may retain legacy references; the 1.7 helper is the supported build path for this snapshot. Legacy source, where included, is separate and has not been newly tested. Assets have separate terms in ASSETS.md.

## Optional input diagnostics

Enable **Temporarily log touch diagnostics to Debug log** in RimTouch settings, reproduce the issue, then use **Copy compact touch diagnostics** (Chinese: **复制精简触控诊断**). This copies JSON without per-entry game stack traces; the normal Debug log remains available. No launch arguments, restart or separate output file are needed. The temporary checkbox is not saved and capture stops after 1024 records; enable it again for a new capture. Existing command-line/file capture remains optional. See [the capture guide](docs/input-diagnostics.md) for fields and limits.
