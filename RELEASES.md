# Releases

## Where builds live

Pre-release test builds and stable releases are published on GitHub Releases:

https://github.com/Stella-sea/rimworld-rimtouch/releases

- Publishing on GitHub does **not** automatically update Steam Workshop.
- Download `RimTouch-fix-test<N>.zip` (pre-release) or `RimTouch-v<VERSION>.zip` (stable) and extract it straight into `RimWorld/Mods/` (the archive contains a single `RimTouch/` root folder with `About/`, `Assemblies/RimTouch.dll`, and `Languages/`).

## Branch & PR flow (`feat/*` → `dev` → `main`)

| Branch | Role | Merge strategy | Protection |
| --- | --- | --- | --- |
| `main` | Verified stable baseline (only receives hardware-tested changes) | **Create a merge commit** from `dev` (never squash `dev → main`, so `main` and `dev` keep identical commit IDs and tags) | PR required, `ci / verify` required, no force-push, no delete |
| `dev` | Active integration & pre-release line (`v0.1.0-test.<N>`) | **Squash and merge** from topic branches (`feat/*`, `fix/*`, `refactor/*`) | `ci / verify` required, no force-push, no delete |
| `feat/*`, `fix/*`, `refactor/*` | Short-lived work branches forked from `dev` | Auto-deleted after PR merge | Verified by PR CI |

## Versioning & tag scheme

- **Pre-release tags (cut on `dev`):** `v<MAJOR>.<MINOR>.<PATCH>-test.<N>` — e.g. `v0.1.0-test.15`.
  - **The dot in `test.15` is load-bearing:** SemVer compares dot-separated numeric identifiers numerically (`test.15` > `test.9`).
  - Marked as GitHub **pre-release**, asset named `RimTouch-fix-test<N>.zip`, title `Test <N> — <short summary>`.
- **Stable tags (cut on `main`):** `v<MAJOR>.<MINOR>.<PATCH>` — e.g. `v0.1.0`.
  - Before cutting a stable tag, revert the test-only `"测试版"` marker in `Languages/ChineseSimplified/About/About.xml` (`release-tag-check.yml` and `package-release.ps1` enforce this automatically).
- **`About/About.xml` `<modVersion>`** carries the exact version without leading `v` (`0.1.0-test.15` or `0.1.0`). `release-tag-check.yml` rejects any tag whose `<modVersion>` or `CHANGELOG.md` entry does not match.
- **Tags are immutable:** protected by GitHub Ruleset (`refs/tags/v*`); never move, delete, or reuse a tag.

## Two-tier CI & local deterministic release pipeline

RimWorld 1.7 assemblies (`Assembly-CSharp.dll`, `UnityStubs.dll`) and `0Harmony.dll` are proprietary and must never be committed or uploaded to public CI. Verification is split cleanly into two tiers:

1. **Cloud CI on every Push / PR / Tag (`.github/workflows/ci.yml` & `release-tag-check.yml`):**
   - `scripts/check-repo-hygiene.ps1` — blocks binaries/logs/local paths, validates `About.xml` and 100% English/Chinese XML key parity.
   - `scripts/check-csharp-syntax.ps1` — parses all `Source/**/*.cs` files with Roslyn `CSharpSyntaxTree` and checks Harmony bootstrap isolation.
   - `scripts/test-input-proof.ps1`, `scripts/test-ui-touch.ps1`, `scripts/test-settings-layout.ps1` — compiles production touch, UI scroll, tap-dedup, HUD, diagnostics, and settings layout code against lightweight stubs.
2. **Local Build, Harmony Audit & Packaging (Windows / Linux with licensed RimWorld 1.7):**
   ```powershell
   # 1. Bump <modVersion> in About/About.xml, then compile deterministically:
   pwsh -NoProfile -File ./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'

   # 2. Verify all [HarmonyPatch] targets and parameter bindings against RimWorld 1.7:
   pwsh -NoProfile -File ./scripts/test-harmony-patches.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'

   # 3. Package verified release zip (verifies Build-Receipt.json & Source/*.cs hashes):
   pwsh -NoProfile -File ./scripts/package-release.ps1 -Version '0.1.0-test.15'
   ```
   *(Linux bash equivalent: `scripts/package-release.sh <version> [staging-dir]`.)*

## Cutting a release & syncing `dev` → `main`

1. Merge topic PRs into `dev` and ensure `CHANGELOG.md` + `About/About.xml` `<modVersion>` are updated for the target version.
2. Run the 3 local commands above (`Build17.ps1` → `test-harmony-patches.ps1` → `package-release.ps1`) and record `DLL SHA256` and `ZIP SHA256` in `CHANGELOG.md`.
3. Commit, push to `dev`, and wait for `ci / verify` to pass.
4. Create an **annotated** tag and push it:
   ```powershell
   git tag -a v0.1.0-test.15 -m "Test 15: <summary>"
   git push origin v0.1.0-test.15
   ```
5. Publish the GitHub pre-release with the packaged zip:
   ```powershell
   gh release create v0.1.0-test.15 --prerelease --title "Test 15 — <summary>" --notes-file <notes-file> .build/releases/RimTouch-fix-test15.zip
   ```
6. Once hardware acceptance passes on the tablet, open a PR **`dev` → `main`** and merge using **Create a merge commit** so `main` advances cleanly to the verified release commit without diverging from `dev`.
