# Releases

## Where test builds live

Test builds are published as **GitHub pre-releases on this fork**:

https://github.com/Stella-sea/rimworld-rimtouch/releases

- The Steam Workshop is **not** updated from GitHub.
- Download the `RimTouch-fix-test<N>.zip` asset from the release you want and
  extract it into RimWorld's `Mods/` folder.

## Versioning scheme

- **Tags:** `v<MAJOR>.<MINOR>.<PATCH>-test.<N>` — e.g. `v0.1.0-test.12`.
- `0.1.0` is the next stable version this work is heading toward
  (`0.x` = pre-stable, anything may change).
- `-test.<N>` is the SemVer pre-release identifier. `N` continues the
  test-build numbering the project has used since test1 (test12 → `test.12`).
- **The dot in `test.12` is load-bearing.** SemVer compares dot-separated
  numeric identifiers numerically, so `test.12` correctly sorts after
  `test.9`. A single suffix like `test12` would sort lexically
  (`test12` < `test9`) and break ordering.
- `About/About.xml` carries the same version (without the `v` prefix) in
  `<modVersion>`. This is the version RimWorld shows in the mod list, so
  the in-game version always matches the release tag.
- Release assets keep the established filename `RimTouch-fix-test<N>.zip`;
  release titles are `Test <N> — <short summary>`.
- Every test build is marked as a GitHub **pre-release** — never a
  stable / "latest" release.
- **Tags are immutable:** never move, delete, or reuse a release tag.
- When this work is ready to go upstream, the stable release drops the
  suffix: `v0.1.0`. The temporary Chinese "测试版" About marker
  (commit `fc7700a`) is reverted before any upstream PR — it exists only
  to tell test builds apart in-game.

## How to cut a test release

1. Land the fix on `fix/touch-issues` and push.
2. Bump `<modVersion>` in `About/About.xml` to the new version
   (e.g. `0.1.0-test.13`). Commit and push.
3. Build with `Build17.ps1` (the supported build path; a Linux equivalent
   lives alongside the local build helper, outside this repo). Expect
   0 errors; record the `RimTouch.dll` SHA256.
4. Run the simulation suites (`twist-test`, `globe-test`, `anchor-test` —
   kept with the build helper, outside this repo) and record the results.
5. Package `About/` + `Assemblies/RimTouch.dll` + `Languages/` into
   `RimTouch-fix-test<N>.zip`. Record the zip SHA256.
6. Create an annotated tag and push it:
   `git tag -a v0.1.0-test.<N> -m "Test <N>: <summary>"` then
   `git push origin v0.1.0-test.<N>`.
7. Create the pre-release:
   `gh release create v0.1.0-test.<N> --prerelease --title "Test <N> — <summary>" --notes-file <notes-file> RimTouch-fix-test<N>.zip`
8. Open the Releases page and download the asset once to confirm it is
   the right file.

## Release-notes skeleton (hand-written for test builds)

Auto-generated notes cannot say what needs testing, so test releases get
hand-written notes:

- What changed (vs the previous test build)
- What to test (numbered, concrete)
- Known issues / still under observation
- Install: extract into `Mods/`, enable Harmony + this mod, only one
  RimTouch implementation
- Build info: source commit, `RimTouch.dll` SHA256, zip SHA256, test results

`.github/release.yml` defines the changelog categories used when notes are
generated from commits (`--generate-notes`). It is a starting point, not a
replacement for the hand-written notes above.

## Why there is no CI build

- The build needs proprietary RimWorld 1.7 assemblies and the Harmony DLL
  from a licensed game install. Those files are not in this repo and must
  **never** be committed — RimWorld is not open source.
- GitHub Actions runners have no licensed RimWorld install, and fetching
  the game in CI would need Steam credentials and would redistribute
  proprietary files. So there is no automated build workflow; the release
  flow is local build + `gh release create`.
- If a clean way to supply the game assemblies ever appears (e.g. a
  private artifact the owner uploads by hand), a tag-triggered build
  workflow can be added then. Until then, do not add CI that pretends
  to build.
