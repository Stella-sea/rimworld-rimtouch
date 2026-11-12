# Changelog

All notable changes to this fork, newest first. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versioning rules in `VERSIONING.md`. Dates are Asia/Shanghai.
`testN` entries are prereleases cut from `fix/touch-issues`;
a version without a `-testN` suffix is a stable release.

## [Unreleased]

- Nothing yet. The next test build goes here first, then moves under
  its version heading when tagged.

## [0.1.0-test.13] — 2026-11-12

Six fixes from the test12 playtest on the Dell Latitude 7210.
Settings save version 9. Build: `build17.sh`, 0 diagnostics.
`RimTouch.dll` SHA256
`654dab5869e554f94606f7cfeb9d2c8ccdce3d3c0eb667eac27df910c2a0cafa`.
Asset `RimTouch-fix-test13.zip` SHA256
`81e2ed3b8588de541417c818f316a513bb7d473a3b488cec02be6b9029aba993`.

### Fixed

- **Slider mis-trigger (test12: not fixed):** scrolling that starts on a
  slider no longer changes the slider's value. Root cause: the test8 fix
  only consumed `MouseDrag` *after* the scroll was claimed, but the
  scroll was only claimed after the finger moved 18 px — the slider
  read every drag in that 18 px window (RimWorld's `HorizontalSlider`
  changes value on `MouseDrag` while `sliderDraggingID` is captured).
  The scroll is now claimed early, once the drag direction is clearly
  along a scrollable axis (mostly vertical on a vertically scrolling
  view, past a 6 px slop); a drag along the slider's own axis is left
  alone for the slider. Verified by a 12-case unit check.
- **Return-to-north is now smooth, not an instant snap (test12: too
  fast, stuttered):** with dual-axis mode off, a single-finger drag
  after a twist no longer sets `lockNorthUp = true` at once (the
  vanilla driver then called `RotateSoNorthIsUp(interpolate: false)`
  every frame — a jump). Instead the view eases back to north-up with
  an exponential approach (the Google Maps / Unity standard for
  re-orienting a camera: eased animation, never a snap), recomputed
  from the current rotation every frame so it follows the drag
  (hand-following) instead of fighting it, and hands the lock back to
  vanilla once aligned (within 0.5°). Simulation: a 45° roll reaches
  95% in exactly the configured duration, no overshoot, no per-frame
  jump.
- **Dual-axis jitter (test12 fix incomplete):** single-finger drags
  with dual-axis on still jittered. Root cause: the vanilla
  `lockNorthUp` flag was left on (by an earlier dual-axis-off drag),
  so the vanilla driver snapped the rotation to north-up every frame
  while the roll-free turntable tried to rotate freely — the two
  fought every frame. Dual-axis drags now turn the vanilla lock off
  at drag start (the turntable manages north itself). Also fixed two
  smaller jitter sources in the same path: after a two-finger twist
  the pan anchor is re-acquired by raycasting at the current
  midpoint (recomputing it from the pre-twist world point pinned a
  different geographic point, so the pan chased the wrong spot), and
  the projected turntable's sensitivity gain is clamped to 1× (a
  feedback loop must never overshoot the finger; above 1× it
  oscillated).
- **Entry-screen crash:** double-tapping on the main menu /
  landing-site page threw `InvalidCastException` (`Find.ColonistBar`
  goes through `Find.MapUI`, which casts the UI root to
  `UIRoot_Play`, but entry screens use `UIRoot_Entry`) and dropped
  the whole touch update. `TryJumpToColonistBarTouchDoubleTap` is now
  guarded the same way test3 guarded `IsVanillaMapToolActive` (only
  touch the colonist bar while in play).

### Added

- **Auto-return to north toggle** (default on): turns the whole
  single-finger auto-return-to-north feature (dual-axis off) on or
  off. Off: the view stays where the two-finger twist left it, with
  no automatic return.
- **Return-to-north duration slider** (0.3–2.0 s, default 0.8 s):
  how long the smooth return takes. Lower is snappier, higher is
  gentler. Both new settings have Chinese + English text; the
  translation files are now 35 keys each, sets identical.

### Notes

- The log's "Simplified Chinese has 3 errors" warning is **not**
  RimTouch's: both translation files are 31 → 35 keys, identical
  sets, no reference to any removed key. It comes from another mod.
- Simulation evidence: twist-test 9/9, globe-test 15/15,
  anchor-test direction + turntable + projected turntable unchanged
  (vertical drag grab error 8–10 px, tilt change 0.2–0.3°),
  smooth-return cases (45°/60° rolls at 0.3/0.8/2.0 s) all converge
  with no overshoot, slider early-claim 12/12 unit cases.

### Test on the 7210

1. Scroll a settings page starting on a slider — the slider value
   must not move; dragging the slider itself still works.
2. Twist the globe with two fingers, then single-finger drag with
   dual-axis off — the view should ease back to north-up over about
   0.8 s, smoothly, with no snap or stutter. Try the duration slider
   (0.3 s vs 2.0 s) and the auto-return toggle off.
3. Single-finger drag with dual-axis on — no jitter, even after a
   twist and after switching back from dual-axis off.
4. Two-finger twist, then keep dragging with two fingers — the globe
   should not drift after the twist.
5. Double-tap on the main menu / landing-site page — no error in the
   log, taps still work.

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
