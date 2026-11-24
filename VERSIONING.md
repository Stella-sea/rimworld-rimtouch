# Versioning and releases — RimTouch fork (Stella-sea/rimworld-rimtouch)

`RELEASES.md` defines the step-by-step release workflow. This companion document explains the design invariants behind the versioning, branching, and verification rules. If the two ever disagree, `RELEASES.md` wins.

## Versioning invariants

- **Tags:** `v0.1.0-test.<N>` for pre-releases (e.g. `v0.1.0-test.15`), and `v0.1.0` when the stable milestone is ready.
- **Dot-separated numeric pre-release (`-test.<N>`):** required so SemVer sorts `test.15` numerically after `test.9`. Never use `test15` without the dot in tags or `<modVersion>`.
- **Single source of version truth at tag time:** `<modVersion>` in `About/About.xml` (without leading `v`) and `## [<version>]` in `CHANGELOG.md` must match the git tag. `.github/workflows/release-tag-check.yml` enforces both automatically on tag push.
- **Release archive naming:** pre-releases keep `RimTouch-fix-test<N>.zip` (single `RimTouch/` root folder containing `About/`, `Assemblies/RimTouch.dll`, and `Languages/`); stable releases use `RimTouch-v<MAJOR>.<MINOR>.<PATCH>.zip`.
- **Immutable tags:** protected on GitHub via the `immutable-release-tags` ruleset (`refs/tags/v*`). Never move, delete, or reuse any tag.

## Two different version numbers — plus `settingsVersion`

1. **RimWorld version (`1.7`):** game target in `<supportedVersions><li>1.7</li></supportedVersions>`.
2. **Mod version (`0.1.0-test.15`):** release version in git tags, `<modVersion>`, GitHub Releases, and `CHANGELOG.md`.
3. **`settingsVersion` (`Source/RimTouchMod.cs`, currently `9`):** save-migration counter for player config files. Only increment when migrating persisted setting defaults — never bump it merely to match a release tag.

## Branch architecture (`main` vs `dev`)

- **`main`:** protected verified baseline. Receives changes via Pull Request from `dev` after tablet acceptance (merged with **Create a merge commit** so `dev` and `main` share commit SHAs and tags never orphan).
- **`dev`:** active integration and pre-release branch (replaces the earlier `fix/touch-issues` branch). Daily features/fixes land on `feat/*`, `fix/*`, or `refactor/*` branches and merge into `dev` via PR (**Squash and merge**).
- **Upstream (`SewSekSea/rimworld-rimtouch` / `iLtanaka/RimTouch`):** upstream issues or PRs still require explicit owner approval (issue first, then PR, with test-only markers removed). GitHub Releases here never update Steam Workshop.

## Packaging & receipt verification

`scripts/package-release.ps1` (cross-platform PowerShell 7) and `scripts/package-release.sh` (POSIX shell) package `RimTouch-fix-test<N>.zip`. `package-release.ps1` cross-checks `.build/1.7/RimTouch.dll` against `.build/1.7/Build-Receipt.json` (`COMPILED`, `0` diagnostics, matching `productSHA256`, and matching SHA256 for every `Source/**/*.cs` file) so a stale DLL or mismatched `<modVersion>` can never be packaged.

## Two-tier verification (Cloud CI + Local RimWorld Build)

Compiling `RimTouch.dll` requires proprietary RimWorld 1.7 and Harmony assemblies that cannot be placed in git or public GitHub Actions. Instead:

- **GitHub Actions (`ci.yml` & `release-tag-check.yml`)** runs on every push/PR to `main` and `dev` and on every `v*` tag: repository/security hygiene, Chinese/English XML key parity, Roslyn `CSharpSyntaxTree` parsing across all `Source/*.cs`, and the 3 isolated Roslyn behavioral/layout test suites (`test-input-proof.ps1`, `test-ui-touch.ps1`, `test-settings-layout.ps1`).
- **Local release verification (`Build17.ps1` + `scripts/test-harmony-patches.ps1`)** compiles deterministically against the licensed RimWorld 1.7 install, verifies every `[HarmonyPatch]` target and parameter binding via reflection against `Assembly-CSharp.dll`, and records the deterministic `DLL SHA256` in `CHANGELOG.md` and release notes.

## Tag message template (test release)

```text
Test 15: UI draggable dedup, accurate HUD hit-testing, nested scroll & camera fixes

Commit: <sha>
Built: 2026-11-24, .NET SDK, 0 diagnostics, 32/32 Harmony patches verified
DLL SHA256: <dll-sha256>
ZIP SHA256: <zip-sha256>
Checks: hygiene PASS, C# syntax PASS, ui-touch PASS, input-proof PASS, settings-layout PASS
Tested on Latitude 7210: RimWorld 1.7.4995 rev309
```
