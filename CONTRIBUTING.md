# Contributing to this fork

This repository maintains the Windows touch continuation of RimTouch for RimWorld 1.7, validating fixes on real touch hardware before considering upstream contributions.

## Branching, Pull Requests, and Syncing

- **`main` (Stable Baseline):** protected branch holding hardware-verified releases only. Never push directly to `main`.
- **`dev` (Integration & Pre-release Branch):** default base for ongoing work and `v0.1.0-test.<N>` pre-releases.
- **Topic branches (`feat/*`, `fix/*`, `refactor/*`, `docs/*`, `ci/*`):**
  1. Branch off `dev`: `git checkout -b fix/<topic> dev`
  2. Use Conventional Commits (`feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `ci:`, `chore:`).
  3. Open a Pull Request into `dev`, ensure `ci / verify` passes and review threads are resolved, then merge via **Squash and merge**.
- **Promoting `dev` → `main`:**
  - After a pre-release (`v0.1.0-test.<N>`) passes real-device testing on the Dell Latitude 7210, open a Sync PR from `dev` into `main`.
  - Always merge `dev → main` using **Create a merge commit** (never Squash), keeping `dev` and `main` on the same commit graph so release tags remain ancestors of `main` and subsequent syncs stay conflict-free.

## Upstream policy

- Nothing goes upstream (`SewSekSea/rimworld-rimtouch`) without local compilation, hardware verification on the Dell Latitude 7210, and explicit owner approval (**issue first, then PR**).
- Retain `GPL-3.0-or-later`, original/continuation attribution, `packageId` (`cinder.rimtouch17.private`), and `About/PublishedFileId.txt`.
- Revert test-only markers (`Languages/ChineseSimplified/About/About.xml` `"测试版"`) before cutting a stable `v0.1.0` tag or opening an upstream PR.
- GitHub Releases on this fork do **not** update Steam Workshop.

## Building, Testing, and Gesture Design

1. **Automated repo & regression checks (no game install required):**
   ```powershell
   pwsh -NoProfile -File ./scripts/check-repo-hygiene.ps1
   pwsh -NoProfile -File ./scripts/check-csharp-syntax.ps1
   pwsh -NoProfile -File ./scripts/test-input-proof.ps1
   pwsh -NoProfile -File ./scripts/test-ui-touch.ps1
   pwsh -NoProfile -File ./scripts/test-settings-layout.ps1
   pwsh -NoProfile -File ./scripts/test-world-touch.ps1
   pwsh -NoProfile -File ./scripts/test-world-touch.ps1 -VerifyRegression
   ```
2. **Full compilation & Harmony patch verification (requires licensed RimWorld 1.7 + `0Harmony.dll`):**
   ```powershell
   pwsh -NoProfile -File ./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
   pwsh -NoProfile -File ./scripts/test-harmony-patches.ps1 -GameDir 'PATH/TO/RIMWORLD' -HarmonyDll 'PATH/TO/0Harmony.dll'
   ```
3. **Gesture standards:** follow established touch conventions (Google Maps / Blender turntable math, nested scroll handoff, frame-level multi-pass deduplication). Ship user-tunable gesture parameters with synced English and Simplified Chinese strings in `Languages/`. Compilation and stub tests verify logic and API bindings only — actual touch feel is always verified on hardware.
