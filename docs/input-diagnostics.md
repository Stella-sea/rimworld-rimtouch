# Touch and UI-scroll diagnostics

In RimTouch settings, enable **Temporarily log touch diagnostics to Debug log**
(Chinese: **临时记录触控诊断（调试日志）**). Reproduce briefly, then press
**Copy compact touch diagnostics / 复制精简触控诊断** and paste into the report.
The bounded in-memory copy contains JSON only, without a stack trace for each
message. The normal game Debug log still contains `[RimTouch.Input]` entries and
is useful when an exception occurs. No arguments, restart or extra file needed.

The checkbox is temporary and not persisted. Uncheck to stop; the capture also
stops after 1024 records or a diagnostic error. Enable again to clear the copy
buffer and start a fresh session. Copy before restarting capture. The copy buffer
is limited to 1,000,000 characters: unusually large records may still appear in
the normal log but not the compact copy. It remains available after stopping.
Changing settings/clipboard does not write a separate player log file.

## Capture and report

1. Enable capture in settings.
2. Reproduce horizontal slider dragging, a vertical content swipe starting on a
   slider, or a new window-border drag after lifting completely.
3. Stop capture and copy the compact diagnostics. Include the visible symptom,
   game version, window mode, UI/system scale and touch device.
4. If the compact text is incomplete or an exception appeared, also copy the
   relevant normal game log. Do not send saves or sensitive settings contents.

Install both the built DLL and its `Languages/` XML files when testing translated
settings text. Building alone does not install anything or start the game.

## Schema 2

- `captureStart`: session, UTC start, schema version, module MVID and screen size.
- `contactFrame`: native contact edges/cancellation and sampled motion, in
  bottom-left framebuffer pixels. Contact IDs distinguish physical contacts.
- `touchLifecycle`: `begin`, `disappeared`, `abort`, and pre-cleanup `reset`.
  `reset` describes the state **before** clearing. `gesture` is a driver sequence.
  `owner` identifies UI, local/world map, or local/world tool ownership; it is
  separate from the gesture `mode`. A normal release retains ownership for the
  existing native-input release guards; cancellation clears it.
- `mapTapDecision`: `gesture`, independent `tap` sequence, physical `finger`,
  local/world view, start/release GUI points and mouse-edge/release flags.
  `released` reserves a map tap; `committed` is the only map submission.
  `uiConsumed` or `uiControl` cancels it after the matching UI event/control.
  `unpairedMouseUp` means no matching mouse-down was observed; it is not permission
  to submit. Other actions explain gesture/context cancellation. A lifecycle
  `owner=WorldMap` alone no longer proves that background selection was submitted.
- `scrollDecision`: frame, gesture, event type and mouse position captured before
  processing; event type afterwards and `dragConsumed`; direction, start/current
  GUI and local positions; viewport/content rectangles; before/after scroll;
  active/inertia state; the **currently drawn owner window**, not the window
  under a potentially unrelated touch. `slot` is only a call ordinal.
- `heartbeat`, `worldTwist`: bounded liveness/twist diagnostics.

`hit=localStart` means the start is inside the unexpanded local viewport;
`none` means outside. This is only a claim eligibility check: an owned gesture
can continue outside. `directionClaim` is the only movement-based claim condition;
the old `distanceClaim`, global-point and mouse OR fallbacks were removed.
Screen information and session metadata live in `captureStart`; subsequent compact
scroll records belong to that capture until the next start. Do not mix schema 1
field meanings with schema 2. Historical findings are in [ui-touch-findings.md](ui-touch-findings.md).

Non-scrollable and unrelated-container GUI passes are omitted. Repeated scroll
decisions are sampled once per 0.3 seconds per owner/rect/decision/hit, regardless
of Layout/Repaint/MouseDrag pass. Claims and lifecycle edges are retained. Native
motion is sampled every 0.1 seconds; edges/cancellation bypass sampling. All
destinations share 1024 records, so this is not a complete per-frame replay.

Before/after covers RimTouch's prefix only, not later native/other-mod work.
Diagnostics do not record slider values, settings labels or text input, and do
not prove compatibility or identify every native window-drag handler.

## Optional compatibility flags

`-rimtouch-debug-log` keeps normal log output enabled independently of the checkbox.
`-rimtouch-input-proof` appends `RimTouch-Input-Proof.jsonl` in the save-data folder.
Both remain optional; normal testing needs neither. Explicit flags stay active
after unchecking. File captures are separated by session markers, not erased.

## Repository checks

- `pwsh -NoProfile -File scripts/test-input-proof.ps1`: actual logger with stubs,
  routing, runtime start/stop/restart, valid JSON, bounds and failure isolation.
- `pwsh -NoProfile -File scripts/test-ui-touch.ps1`: actual scroll/tap policy and
  coordinate helper with synthetic GUI/contact stubs. Regression cases cover
  scaled coordinate displacement, horizontal/edge rejection, vertical claim,
  late MouseDown, window ownership, slider jitter and repeated toggles.
  It also compiles the real pending map-tap implementation and Entry/Play GUI
  patches: UI-first submission, controls outside registered windows, independent
  GUI/contact edge orderings, delayed paired mouse-up, cancellation, unrelated
  UI activity, and mouse fallback. `-VerifyRegression` runs four independent
  behavioral negative controls, including early map submission and missing claims.

These do not run RimWorld/Harmony GUI dispatch, a native callback or a touchscreen.
