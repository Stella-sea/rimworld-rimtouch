# Versioning and releases — RimTouch fork (Stella-sea/rimworld-rimtouch)

The release scheme is defined in `RELEASES.md` (user-facing: where
builds live, how to cut one). This file is the companion detail:
the reasoning behind the scheme, the things `RELEASES.md` does not
spell out, and the packaging script. If the two ever disagree,
`RELEASES.md` wins and this file gets fixed — that happened once
already, when a parallel draft used `v0.2.0-test12` and was corrected
to the scheme below.

## The scheme (summary — see RELEASES.md for the full text)

- **Tags:** `v0.1.0-test.<N>` — e.g. `v0.1.0-test.12`. `0.1.0` is the
  next stable version this work is heading toward (`0.x` = pre-stable);
  the test builds are its prereleases. The stable release drops the
  suffix: `v0.1.0`.
- **The dot in `test.12` is load-bearing.** SemVer compares
  dot-separated numeric identifiers numerically, so `test.12` sorts
  after `test.9`. A single suffix like `test12` would sort lexically
  (`test12` < `test9`) and break ordering. Do not use the `testN`
  form in tags.
- **`About/About.xml` carries the same version** (without the `v`
  prefix) in `<modVersion>` — `0.1.0-test.12`. This is the version
  RimWorld shows in the mod list, so the in-game version always
  matches the release tag. Bump `<modVersion>` in the same commit
  series as the release; never ship a tag whose `modVersion` says
  something else.
- **Assets keep the established filename** `RimTouch-fix-test<N>.zip`
  (the name she already knows from test1–test12). The zip contains a
  single root folder `RimTouch/` — extract straight into
  `RimWorld/Mods/` — which the old loose-file zips did not.
- Every test build is a GitHub **prerelease**, never a stable /
  "latest" release. Tags are immutable: never move, delete or reuse
  one. Never reuse a `test.<N>` number.

## Two different "1.7"s — do not confuse them

- **RimWorld 1.7** is the *game* version. It lives in
  `About/About.xml` (`<supportedVersions><li>1.7</li></supportedVersions>`)
  and never changes with our fixes.
- **The mod version** (`0.1.0-test.12`) is *our* version, independent
  of the game version. It lives in the git tag, `<modVersion>`, the
  release name and `CHANGELOG.md`.

## `settingsVersion` is not the mod version

`settingsVersion` in `Source/RimTouchMod.cs` is a *save-migration*
counter, not the mod version. It only moves when a settings default
or migration needs it (currently 8). Do not bump it to match a tag,
and do not read a tag off it.

## Branches

- `main` — stable baseline only (currently the upstream snapshot).
  Nothing is merged here until a test line is approved.
- `fix/touch-issues` — the test line. Test releases are cut from here.
- Upstream contribution still goes through the issue-first flow she
  set on 2026-11-07: issue, then PR, only with her approval. A GitHub
  Release in this fork does **not** update Steam Workshop.

## Packaging

`scripts/package-release.sh <version>` builds the release asset from
a staging directory (default: `staging-0.1.0-test.12` under the
research workspace, built from the tagged tree + the fresh DLL) — `RimTouch-fix-test<N>.zip` with a single root
folder `RimTouch/`. It verifies the staged DLL hash matches the fresh
local build in `.build/1.7/RimTouch.dll` and that `About.xml` carries
the right `packageId` and `supportedVersions`, then prints the zip
SHA256 for the release notes. If the DLL hashes differ, it stops:
the zip must be the build from the tagged commit, not a stale DLL.

## Why there is no CI build

GitHub Actions cannot build this mod: compiling needs the licensed
RimWorld 1.7 assemblies and the Harmony DLL, which are proprietary
and cannot live in the repo or in CI. The workflow in
`.github/workflows/release-tag-check.yml` therefore only validates
release hygiene on tag push — SemVer tag shape, a `CHANGELOG.md`
entry for the version, and that the tag is annotated. (It is not a
build workflow, and `.github/release.yml` is only the config for
auto-generated release-note categories, not a workflow either.)
Build evidence is the local deterministic build: the DLL SHA256 in
the release notes must match a rebuild from the tagged commit.

## Tag message template (test release)

```
Test 12: flat settings, dual-axis rename, jitter + north-lock fixes

Commit: dc20615 (+ release docs)
Built: 2026-11-12, .NET SDK 10.0.403, 0 diagnostics
DLL SHA256: afb9c4e105f6fe59f7afd441101138b2199ebb3b7391517a2fd57d1e2748742f
Simulations: twist 9/9, globe 15/15
Test on 7210: settings page flat + renamed to dual-axis mode;
single-finger globe drag with dual-axis on (jitter?); vanilla
north-lock returns after two-finger twist with dual-axis off;
debug-window scroll.
```

## History of the old way (why these files exist)

Before 2026-11-12, test builds went out as `RimTouch-fix-testN.zip`
through expiring `muse.ai` file links, with loose files at the zip
root (no `RimTouch/` folder, so installing meant creating the folder
by hand) and no tag, no changelog entry and no permanent download
page. `test12` is the first build shipped under this scheme; the old
zips are superseded, not re-tagged.
