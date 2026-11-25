# Releases

## Where builds live

Pre-release test builds and stable releases are published on GitHub Releases:

https://github.com/Stella-sea/rimworld-rimtouch/releases

- Publishing on GitHub does **not** automatically update Steam Workshop.
- Current stable version: **0.2.0**, asset `RimTouch-v0.2.0.zip`.
- Download `RimTouch-fix-test<N>.zip` (pre-release) or `RimTouch-v<VERSION>.zip` (stable) and extract it straight into `RimWorld/Mods/` (one `RimTouch/` root with `About/`, `Assemblies/RimTouch.dll`, `Languages/`, `LICENSE`, `ASSETS.md` and `CREDITS.md`).

## Branch & PR flow (`feat/*` → `dev` → `main`)

| Branch | Role | Merge strategy | Protection |
| --- | --- | --- | --- |
| `main` | Verified stable baseline (only receives hardware-tested changes) | **Create a merge commit** from `dev` (never squash `dev → main`; preserve shared history and tag ancestry) | PR required, `ci / verify` required, no force-push, no delete |
| `dev` | Active integration & pre-release line | **Squash and merge** from topic branches | `ci / verify` required, no force-push, no delete |
| `feat/*`, `fix/*`, `refactor/*`, `chore/*` | Short-lived work branches forked from `dev` | Auto-deleted after PR merge | Verified by PR CI |

## Versioning & tag scheme

- **Pre-release tags (cut on `dev`):** `v<MAJOR>.<MINOR>.<PATCH>-test.<N>` — e.g. `v0.1.0-test.15`.
  - **The dot in `test.15` is load-bearing:** SemVer compares dot-separated numeric identifiers numerically (`test.15` > `test.9`).
  - Marked as GitHub **pre-release**, asset named `RimTouch-fix-test<N>.zip`, title `Test <N> — <short summary>`.
- **Stable tags (cut on `main`):** `v<MAJOR>.<MINOR>.<PATCH>` — e.g. `v0.2.0`.
  - Before cutting a stable tag, remove candidate/test/private acceptance wording from English and Chinese display metadata. Keep the package ID unchanged; its historical `.private` suffix is part of the identity.
- **`About/About.xml` `<modVersion>`** carries the exact version without leading `v` (for example `0.2.0`). `release-tag-check.yml` rejects any tag whose `<modVersion>` or `CHANGELOG.md` entry does not match.
- **Tags are immutable:** protected by GitHub Ruleset (`refs/tags/v*`); never move, delete, or reuse a tag.

## Two-tier CI & local deterministic release pipeline

RimWorld 1.7 assemblies (`Assembly-CSharp.dll`, `UnityStubs.dll`) and `0Harmony.dll` are proprietary and must never be committed or uploaded to public CI. Verification is split cleanly into two tiers:

1. **Cloud CI on every Push / PR / Tag (`.github/workflows/ci.yml` & `release-tag-check.yml`):**
   - `scripts/check-repo-hygiene.ps1` — blocks binaries/logs/local paths, validates `About.xml` and 100% English/Chinese XML key parity.
   - `scripts/check-csharp-syntax.ps1` — parses all `Source/**/*.cs` files with Roslyn `CSharpSyntaxTree` and checks Harmony bootstrap isolation.
   - `scripts/test-input-proof.ps1`, `scripts/test-ui-touch.ps1`, `scripts/test-settings-layout.ps1`, `scripts/test-world-touch.ps1` — compile production policies/math against lightweight stubs; UI, settings and world suites also run `-VerifyRegression` behavioral negative controls.
2. **Local Build, Harmony Audit & Packaging (Windows / Linux with licensed RimWorld 1.7):**
   ```powershell
   # 1. Bump <modVersion> in About/About.xml, then compile deterministically:
   pwsh -NoProfile -File ./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'

   # 2. Verify all [HarmonyPatch] targets and parameter bindings against RimWorld 1.7:
   pwsh -NoProfile -File ./scripts/test-harmony-patches.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'

   # 3. Package verified release zip (verifies Build-Receipt.json & Source/*.cs hashes):
    pwsh -NoProfile -File ./scripts/package-release.ps1 -Version '0.2.0'
   ```
   PowerShell 7 is the supported packaging entry on Windows and Linux.
   `package-release.sh` is a legacy prerelease helper; it does not include the full notices or receipt checks required by this release pipeline.

## Cutting a stable release & syncing `dev` → `main`

1. Merge topic PRs into `dev` and ensure `CHANGELOG.md` + `About/About.xml` `<modVersion>` are updated for the target version.
2. Run the 3 local commands above (`Build17.ps1` → `test-harmony-patches.ps1` → `package-release.ps1`) and record `DLL SHA256` and `ZIP SHA256` in `CHANGELOG.md`.
3. Submit the release-preparation topic PR into `dev`, squash-merge it after review and passing `ci / verify`.
4. Once hardware acceptance is recorded, open a PR **`dev` → `main`** and merge using **Create a merge commit**, never Squash. Sync the local `main` and verify that the intended version and source are present.
5. Create an **annotated** stable tag on `main` and push it:
   ```powershell
    git tag -a v0.2.0 -m "RimTouch 0.2.0: accepted touch and world-camera fixes"
    git push origin v0.2.0
   ```
6. Wait for `release-tag-check` to pass and publish the stable release with the verified zip:
   ```powershell
    gh release create v0.2.0 --verify-tag --title "RimTouch 0.2.0" --notes-file .build/releases/release-notes-0.2.0.md .build/releases/RimTouch-v0.2.0.zip
   ```

Prereleases instead use annotated `v<version>-test.<N>` tags on `dev`, the
`RimTouch-fix-test<N>.zip` asset, and `gh release create --prerelease`.
Tags/publication are separate authorized actions; local packaging alone does not publish anything.
