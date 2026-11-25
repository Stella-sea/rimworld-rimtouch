# Versioning and releases — RimTouch fork (Stella-sea/rimworld-rimtouch)

`RELEASES.md` defines the step-by-step release workflow. This companion document explains the design invariants behind the versioning, branching, and verification rules. If the two ever disagree, `RELEASES.md` wins.

## Versioning invariants

- **Tags:** annotated `v<MAJOR>.<MINOR>.<PATCH>-test.<N>` on `dev` for prereleases, and `v<MAJOR>.<MINOR>.<PATCH>` on `main` for stable releases. Current stable version: `0.2.0`.
- **Dot-separated numeric pre-release (`-test.<N>`):** required so SemVer sorts `test.15` numerically after `test.9`. Never use `test15` without the dot in tags or `<modVersion>`.
- **Single source of version truth at tag time:** `<modVersion>` in `About/About.xml` (without leading `v`) and `## [<version>]` in `CHANGELOG.md` must match the git tag. `.github/workflows/release-tag-check.yml` enforces both automatically on tag push.
- **Release archive naming:** pre-releases keep `RimTouch-fix-test<N>.zip` (single `RimTouch/` root folder containing `About/`, `Assemblies/RimTouch.dll`, and `Languages/`); stable releases use `RimTouch-v<MAJOR>.<MINOR>.<PATCH>.zip`.
- **Immutable tags:** protected on GitHub via the `immutable-release-tags` ruleset (`refs/tags/v*`). Never move, delete, or reuse any tag.

## Two different version numbers — plus `settingsVersion`

1. **RimWorld version (`1.7`):** game target in `<supportedVersions><li>1.7</li></supportedVersions>`.
2. **Mod version (`0.2.0`):** release version in git tags, `<modVersion>`, GitHub Releases, and `CHANGELOG.md`.
3. **`settingsVersion` (`Source/RimTouchMod.cs`, currently `9`):** save-migration counter for player config files. Only increment when migrating persisted setting defaults — never bump it merely to match a release tag.

## Branch architecture (`main` vs `dev`)

- **`main`:** protected verified baseline. Receives changes via Pull Request from `dev` after tablet acceptance (merged with **Create a merge commit** so `dev` and `main` share commit SHAs and tags never orphan).
- **`dev`:** active integration and pre-release branch (replaces the earlier `fix/touch-issues` branch). Daily features/fixes land on `feat/*`, `fix/*`, or `refactor/*` branches and merge into `dev` via PR (**Squash and merge**).
- **Upstream (`SewSekSea/rimworld-rimtouch` / `iLtanaka/RimTouch`):** upstream issues or PRs still require explicit owner approval (issue first, then PR, with test-only markers removed). GitHub Releases here never update Steam Workshop.

## Packaging & receipt verification

`scripts/package-release.ps1` (cross-platform PowerShell 7) is the supported entry for prerelease and stable archives. It cross-checks `.build/1.7/RimTouch.dll` against `.build/1.7/Build-Receipt.json` (`COMPILED`, `0` diagnostics, matching `productSHA256`, and matching SHA256 for every `Source/**/*.cs` file), checks `<modVersion>` and stable display metadata, and includes `LICENSE`, `ASSETS.md` and `CREDITS.md`. `package-release.sh` remains a legacy prerelease helper and is not equivalent to the supported pipeline.

## Two-tier verification (Cloud CI + Local RimWorld Build)

Compiling `RimTouch.dll` requires proprietary RimWorld 1.7 and Harmony assemblies that cannot be placed in git or public GitHub Actions. Instead:

- **GitHub Actions (`ci.yml` & `release-tag-check.yml`)** runs on every push/PR to `main` and `dev` and on every `v*` tag: repository/security hygiene, Chinese/English XML key parity, Roslyn `CSharpSyntaxTree` parsing across all `Source/*.cs`, diagnostics plus UI/settings/world regression suites, and `-VerifyRegression` for UI, settings and world.
- **Local release verification (`Build17.ps1` + `scripts/test-harmony-patches.ps1`)** compiles deterministically against the licensed RimWorld 1.7 install, verifies every `[HarmonyPatch]` target and parameter binding via reflection against `Assembly-CSharp.dll`, and records the deterministic `DLL SHA256` in `CHANGELOG.md` and release notes.

## Tag message template (stable release on `main`)

```text
RimTouch 0.2.0: world camera, slider and UI/map-tap fixes

Commit: <sha>
Built: <date>, .NET SDK, 0 diagnostics, <count>/<count> Harmony patches verified
DLL SHA256: <dll-sha256>
ZIP SHA256: <zip-sha256>
Checks: hygiene PASS, C# syntax PASS, input-proof PASS, UI/settings/world PASS with negative controls
Hardware acceptance: <reported coverage and untested boundaries>
```
