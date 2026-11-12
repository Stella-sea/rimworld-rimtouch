# Changelog

All notable changes to this fork, newest first. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versioning rules in `VERSIONING.md`. Dates are Asia/Shanghai.
`testN` entries are prereleases cut from `fix/touch-issues`;
a version without a `-testN` suffix is a stable release.

## [Unreleased]

- Nothing yet. The next test build goes here first, then moves under
  its version heading when tagged.

## [0.1.0-test.12] — 2026-11-11

First build shipped as a GitHub prerelease (`v0.1.0-test.12`).
Commit `dc20615` on `fix/touch-issues`. Settings save version 8.

### Fixed

- Settings page back to a flat list: removed the test8 grouping
  headings and the duplicated "Enable touch" entry, so all settings
  are visible again.
- Renamed "lock north up while dragging" to **双轴模式 (dual-axis
  mode)**. The save field name is unchanged, so existing settings
  are kept.
- **Dual-axis jitter fixed (root cause found):** dragging used to
  compute the globe anchor first and then `SnapNorthToUp` rolled the
  globe, leaving the anchor stale — the projected turntable chased a
  point that no longer existed, every frame. The "snap north on drag
  start" step is deleted entirely (field, setting and code); dual-axis
  mode never rolls, so it never needed the snap.
- **Vanilla north-lock now returns after a two-finger twist:** the
  vanilla `lockNorthUp` flag was only ever set to `false` inside the
  twist path and never set back to `true` anywhere. With dual-axis
  mode off, starting a single-finger drag now sets it back to `true`.
- Refresh the local anchor after a two-finger twist, so the next
  pan does not start from a stale anchor.
- Debug-window drag-scroll: the debug window routes through
  `DevGUI.BeginScrollView`, not `Widgets.BeginScrollView`, so the
  scroll patch never saw it. Both paths are now hooked (also in
  this build from `fe8becc`).

### Test checklist for the 7210 (from StellaSea, 2026-11-11)

1. Settings page complete, dual-axis rename reads naturally.
2. Dual-axis on: single-finger globe drag no longer jitters.
3. Dual-axis off: after a two-finger twist, switching back to one
   finger restores the vanilla north-lock.
4. Panning stays steady after a two-finger twist.

### Build evidence

- Build: `build17.sh`, .NET SDK 10.0.403, 0 diagnostics.
- DLL SHA256: `afb9c4e105f6fe59f7afd441101138b2199ebb3b7391517a2fd57d1e2748742f`
- Release asset: `RimTouch-fix-test12.zip` (single `RimTouch/` folder),
  SHA256 `031ec23638843fe5512964e69306cfa0183cc760220aaeb261e39b1c3c23c491`;
  `About.xml` carries `<modVersion>0.1.0-test.12</modVersion>`.
- Simulations: twist 9/9, globe 15/15. Simulation verifies logic only;
  gesture feel still needs the 7210.

## [0.1.0-test.11] — 2026-11-10 (historical, untagged)

- Debug-window scroll fix only (`fe8becc`). Shipped as an expiring
  file link, never tagged. Folded into test12; no separate release.

## [0.1.0-test.10] — 2026-11-09 (historical, untagged)

- Turntable drag direction, auto-lock north, UI scroll vs sliders,
  settings sections (`16fd83e` and earlier). Shipped as an expiring
  file link, never tagged.

## Baseline — 2026-10-28 (upstream snapshot, untagged)

- Upstream SewSekSea Windows 1.7 continuation snapshot, as published
  on `main` (`e7e3547`). No fixes from this fork. It is the starting
  point of the `0.1.0` line, not a release itself; the stable `v0.1.0`
  tag is reserved for the approved build. Earlier `test1`–`test9`
  builds were cut from code between this baseline and `0.1.0-test.10`
  and were shipped as expiring file links, never tagged.
