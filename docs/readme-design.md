# README design and verification

Documentation update for the published **0.2.0** release, checked on
2026-11-25. This note records the README work, not a new game build or hardware
acceptance.

## Layout and language

- [English](../README.md) and [Simplified Chinese](../README.zh-CN.md) have the
  same sections, control rows, settings and download destinations. Each links
  directly to the other language.
- The first screen identifies the platform, game version, stable release and
  Harmony prerequisite, then shows an original gesture diagram. Installation
  and controls precede compatibility and contributor information.
- Two-column tables keep gesture descriptions readable at narrow widths.
  Settings defaults and the source-build instructions use native `details`
  elements. There are no remote badge services, script dependencies or layout
  tables.
- [Original SVG diagrams](images/README.md) use the same geometry with localized
  text, explicit colors, accessible titles/descriptions and Markdown alt text.
  The diagram is schematic; it does not depict a captured game session.
  Existing preview artwork and its terms are unchanged.
- English and Chinese prose were edited using the `humanizer` and
  `Humanizer-zh` skills, retaining the conditions on commands, selection and
  compatibility.

## Evidence for player-facing claims

| Claim | Source checked |
| --- | --- |
| Stable 0.2.0, ZIP name and download link | [Published release](https://github.com/Stella-sea/rimworld-rimtouch/releases/tag/v0.2.0), queried with `gh release view`; `isPrerelease=false`, asset uploaded. |
| One `RimTouch/` archive root, DLL, translations and notices | Inspected the existing release ZIP: 10 entries; SHA256 `cee21c9d982b15fa16f6829b48bfc5628d0db04e4ee7875c3b2b78f1dd31a720`, matching the remote asset digest. No ZIP was rebuilt. |
| Windows, RimWorld 1.7, Harmony, package identity and authors | [About.xml](../About/About.xml), [TouchSource.cs](../Source/Input/TouchSource.cs), [CREDITS.md](../CREDITS.md). |
| Local tap, drafted commands, double tap, hold thresholds, two-finger tap and portrait jump | [TouchInputDriver.cs](../Source/TouchInputDriver.cs): `SelectHoldSeconds`, `RightClickHoldSeconds`, `UpdateOneFinger`, `TryDoDraftedPawnTapCommand`, `TryDoMapDoubleClick`, `TrySelectFromTwoFingerDraftedTap`, `TryJumpToColonistBarTouchDoubleTap`; [UiHitTester.cs](../Source/UiHitTester.cs): `TryCancelVanillaMapToolFromTwoFingerTap`. |
| World tap, drag, pinch, twist and double-tap drag | [TouchInputDriver.cs](../Source/TouchInputDriver.cs): `UpdateOneFingerWorld`, `UpdateTwoFinger`, second-contact interval; [WorldMapTouchCamera.cs](../Source/WorldMapTouchCamera.cs): `ApplyDualAxisTurntableDrag`, `BeginNorthReturnIfNeeded`, `DoWorldLeftClick`. |
| Defaults, slider ranges, resets and zoom ownership | [RimTouchMod.cs](../Source/RimTouchMod.cs): field defaults, `ExposeData`, `DoSettingsWindowContents`, `ApplyExtendedZoomRange`; [English](../Languages/English/Keyed/RimTouch.xml) and [Chinese](../Languages/ChineseSimplified/Keyed/RimTouch.xml) settings strings. |
| Scroll/slider arbitration and window ownership | [TouchInputDriver.UiScroll.cs](../Source/TouchInputDriver.UiScroll.cs) and [TouchInputDriver.cs](../Source/TouchInputDriver.cs). |
| UI-first map taps and custom-control limits | [TouchInputDriver.UiTap.cs](../Source/TouchInputDriver.UiTap.cs), [0.2.0 changelog](../CHANGELOG.md), [touch findings](ui-touch-findings.md). General drag/hold/multitouch control acquisition is still follow-up work. |
| Temporary diagnostics, 1024-record budget, copy and restart behavior | [RimTouchMod.cs](../Source/RimTouchMod.cs), [TouchInputProof.cs](../Source/Input/TouchInputProof.cs), [capture guide](input-diagnostics.md). |
| License and preview restrictions | [LICENSE](../LICENSE), [ASSETS.md](../ASSETS.md), [CREDITS.md](../CREDITS.md); separate notice for the original SVG files. |
| Build path and historical records | [Build17.ps1](../Build17.ps1), [Build status](../BUILD-STATUS.md), [Source snapshot](../Source-Snapshot.json), [Releases](../RELEASES.md), [Contributing](../CONTRIBUTING.md). |

The production source, About metadata, translations, existing asset notices and
build entry match `v0.2.0` (`git diff v0.2.0 -- Source About Languages ASSETS.md
CREDITS.md Build17.ps1 Build.json` returned no changes). There was no existing
issue template under `.github/ISSUE_TEMPLATE/`; both READMEs link to the ordinary
new-issue form and list the useful report fields.

## Checks performed

- Parsed both READMEs, the image notice and this design note with
  `markdown-it-py`; all **74** local links, anchors and image references resolved
  (45 in the READMEs/image notice, 29 in this note). Both READMEs have identical
  block structures, numerical content and external/project-document destinations.
  Manual comparison covered the meaning and scope of each translated row.
- Both SVGs parse as XML, have `title`/`desc`, contain no script or embedded
  artwork, resolve their internal marker/pattern references and have identical
  geometry. Images also have explicit Markdown alt text.
- Local Markdown previews were opened in a browser at desktop width and
  **390 CSS pixels** mobile width. English/Chinese entry text, SVG labels and
  two-column controls were inspected. This used approximate GitHub styling,
  not a published GitHub page.
- Passed `check-repo-hygiene.ps1` (38/38 translation keys),
  `check-csharp-syntax.ps1` (20 source files, zero syntax errors),
  `test-input-proof.ps1`, `test-ui-touch.ps1`, `test-settings-layout.ps1` and
  `test-world-touch.ps1`.
- Also passed `-VerifyRegression` for UI (four negative controls), settings
  (one) and world (three): each regression was rejected by its expected
  behavioral assertion.
- `git diff --check` passed. Final worktree review showed changes only to the
  two READMEs, two SVGs, their license notice and this note. Local preview HTML,
  screenshots and the link-check helper stayed in ignored `.build/` space.

No source changes, game build, packaging, installation or game launch were
needed for this documentation update. After the local previews, the main session
committed and pushed the documentation as `175d681` and opened PR #10 into `dev`.
On the actual GitHub pages, the main session verified the English SVG, expanded
settings table and reset instructions, then followed the Simplified Chinese
language link and checked the Chinese SVG, headings, download links and
installation text at **390 CSS pixels** mobile width. These checks passed after
the SVG loaded; the earlier mobile capture taken before loading completed was
not a rendering failure. Final screenshots are retained in ignored
`.build/readme-preview/` as `github-readme-en-2026-11-25T09-19-25-295.jpg` and
`github-readme-zh-mobile-loaded-2026-11-25T09-20-30-586.jpg`.
Existing hardware acceptance is cited from the release, not repeated or expanded
by this documentation work.
