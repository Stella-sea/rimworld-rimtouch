# Contributing to this fork

This fork exists to make RimWorld 1.7 playable by touch on a Windows
tablet, and to contribute the fixes upstream when they are proven.
The rules below are the ones StellaSea set on 2026-11-07; they are
not negotiable without her say-so.

## Upstream first, but only when proven

- Nothing goes upstream until it is fixed, locally compiled, and
  tested by her on the Dell Latitude 7210 — and she has said yes.
- When it is ready: **issue first, then PR**, against
  `SewSekSea/rimworld-rimtouch`. No issue or PR without her approval.
- PRs keep: GPL-3.0-or-later, packageId
  `cinder.rimtouch17.private` unchanged, and build evidence from
  `Build17.ps1` / `build17.sh` (0 diagnostics + DLL SHA256), with the
  testing limits stated plainly (compiling is not proof of
  touchscreen / scaling / full-mod-list compatibility).
- A GitHub Release here does **not** update Steam Workshop. Workshop
  releases are the upstream author's call.

## Branches and versions

- `main` is the stable baseline. Work on `fix/touch-issues` (or a
  new branch per change set).
- Versions follow `VERSIONING.md` (SemVer; test builds are
  prereleases `v0.2.0-testN`). Never move or reuse a tag.
- Every release gets a `CHANGELOG.md` entry first. No changelog,
  no release.
- The in-game "测试版" marker (`Languages/ChineseSimplified/About/`
  from `fc7700a`) is test-only: revert it before any stable release
  or upstream PR.

## Building and testing

- Build with `./Build17.ps1` (Windows, PowerShell 7) or `build17.sh`
  (Linux). Both need a licensed RimWorld 1.7 install and the matching
  Harmony DLL — that is why there is no CI build (see
  `VERSIONING.md`).
- Run the simulation suites in `twist-test/`, `globe-test/` and
  `anchor-test/` and record the result. Simulations verify logic
  only; gesture feel is decided on the 7210, never by a simulation.
- Check industry-standard practice before implementing a gesture
  (her rule from 2026-11-09: Blender / Google Maps style turntable
  handling, not invented snap-back corrections). New gesture
  behaviour ships with a setting so players can tune it, with
  Chinese + English strings.
