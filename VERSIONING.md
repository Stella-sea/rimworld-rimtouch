# Versioning and releases — RimTouch fork (Stella-sea/rimworld-rimtouch)

How this fork numbers versions and ships test builds, following the
conventions mature projects use (SemVer tags + GitHub Releases +
CHANGELOG). Read this before cutting a release.

## Two different "1.7"s — do not confuse them

- **RimWorld 1.7** is the *game* version. It lives in
  `About/About.xml` (`<supportedVersions><li>1.7</li></supportedVersions>`)
  and never changes with our fixes.
- **The mod version** (e.g. `v0.2.0-test12`) is *our* version, independent
  of the game version. It lives in the git tag, the release name, the zip
  filename and `CHANGELOG.md`. It is deliberately **not** written into
  `About/About.xml`: the in-game "测试版" marker already tells test
  builds apart, and editing `About.xml` for every test build would churn
  the one file RimWorld reads to identify the mod. If a stable release
  ever wants the version visible in-game, that is a separate, deliberate
  change — not something a test build does silently.

## Scheme: SemVer, with test builds as prereleases

Tags are `vMAJOR.MINOR.PATCH[-PRERELEASE]`, e.g.:

| Tag | Meaning |
| --- | --- |
| `v0.1.0` | Baseline: the upstream SewSekSea snapshot as published on `main` (`e7e3547`), no fixes yet. Tagged retroactively so the baseline is addressable. |
| `v0.2.0-test12` | 12th test build of the `0.2.0` line: all fixes on `fix/touch-issues` up to `dc20615`. A GitHub **prerelease** (not a stable release). |
| `v0.2.0-test13`, `v0.2.0-test14`, … | Later test builds on the same line. Only the prerelease number moves. |
| `v0.2.0` | Stable: cut only after StellaSea has approved the build on her 7210 **and** the upstream issue → PR flow has been followed (see CONTRIBUTING.md). |

Rules, borrowed from SemVer / Keep a Changelog practice:

- `PATCH` — bug fixes only, no new settings or behaviour.
- `MINOR` — new features / new settings (e.g. dual-axis mode, turntable
  speed slider, multi-select). The `0.1.0` → `0.2.0` jump is a minor
  bump for exactly this reason.
- `MAJOR` — breaking changes (settings save format, packageId, dropping
  a RimWorld version). `0.x` is still pre-stable, so treat `MINOR` bumps
  as potentially breaking until `1.0.0`.
- Prerelease identifiers are `testN`, continuing the `test1…test12`
  numbering she already knows. Never reuse a `testN` number, never
  move a tag after the zip has been downloaded, and never rewrite a
  tag to a different commit — if a build is wrong, cut `testN+1`.
- `settingsVersion` in `Source/RimTouchMod.cs` is a *save-migration*
  counter, not the mod version. It only moves when a settings default
  or migration needs it (currently 8). Do not bump it to match a tag.

## Branches

- `main` — stable baseline only (currently the upstream snapshot).
  Nothing is merged here until a test line is approved.
- `fix/touch-issues` — the test line. Test releases are cut from here.
- Upstream contribution still goes through the issue-first flow she
  set on 2026-11-07: issue, then PR, only with her approval. A GitHub
  Release in this fork does **not** update Steam Workshop.

## Release checklist (what "doing a release" means here)

1. Code is committed on `fix/touch-issues`; build with `build17.sh`
   (Linux) or `Build17.ps1` (Windows) — 0 diagnostics.
2. Run the simulation suites (`twist-test/`, `globe-test/`,
   `anchor-test/`) and record the result in the release notes.
3. Add a `CHANGELOG.md` entry for the new version (date, commit,
   what changed, what still needs her 7210 test).
4. Package with `scripts/package-release.sh <version>` — it builds
   `RimTouch-v<version>.zip` with a single root folder `RimTouch/`
   (extract straight into `RimWorld/Mods/`), verifies the DLL hash
   matches the fresh build, and prints the SHA256.
5. Create an **annotated** tag and push it:
   `git tag -a v0.2.0-test12 -m "…" && git push origin v0.2.0-test12`
6. Create the GitHub Release from that tag, **mark it as a
   prerelease**, attach the zip, and paste the SHA256 + test checklist
   into the release notes:
   `gh release create v0.2.0-test12 --prerelease --title "v0.2.0-test12" --notes-file <notes> RimTouch-v0.2.0-test12.zip`
   (The release notes template lives in the tag message; see below.)
7. Send her the Releases page link, not a file link:
   `https://github.com/Stella-sea/rimworld-rimtouch/releases`
   — GitHub Releases do not expire, unlike the old `muse.ai` file
   links (which expired after ~24h).

## Why there is no CI build

GitHub Actions cannot build this mod: compiling needs the licensed
RimWorld 1.7 assemblies and the Harmony DLL, which are proprietary and
cannot live in the repo or in CI. So the release workflow
(`.github/workflows/release-tag-check.yml`) only validates the tag —
it checks the tag is valid SemVer and that `CHANGELOG.md` has an entry
for it. The build evidence is the local deterministic build: the DLL
SHA256 recorded in the release notes must match a rebuild from the
tagged commit. Do not claim CI built anything.

## Tag message template (test release)

```
v0.2.0-test12 — test build 12 (prerelease)

Commit: dc20615
Built: 2026-11-11, .NET SDK 10.0.403, 0 diagnostics
DLL SHA256: afb9c4e105f6fe59f7afd441101138b2199ebb3b7391517a2fd57d1e2748742f
Simulations: twist 9/9, globe 15/15
Test on 7210: settings page flat + renamed to 双轴模式; single-finger
globe drag with dual-axis on (jitter?); vanilla north-lock returns
after two-finger twist with dual-axis off; debug-window scroll.
```

## History of the old way (why this file exists)

Before 2026-11-12, test builds went out as `RimTouch-fix-testN.zip`
through expiring `muse.ai` file links, with loose files at the zip
root (no `RimTouch/` folder, so installing meant creating the folder
by hand) and no tag, no changelog entry and no permanent download
page. `test12` is the first build shipped under this scheme; the old
zips are superseded, not re-tagged.
