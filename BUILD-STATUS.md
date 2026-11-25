# Build status

## 0.2.0 release preparation — 2026-11-25

- Base source commit: `979c212fa15b9ba50b255dc27aff506e8dddf91c` (accepted fix
  merged into `dev`); release metadata and packaging changes are prepared on
  `chore/release-0.2.0`. No runtime source content differs from that base.
- Fresh `Build17.ps1` receipt UTC: `2026-11-25T08:47:38.1374882Z`.
  RimWorld `1.7.4995 rev309`, Harmony `3.0.0.0`, 20 production C# sources,
  0 compiler diagnostics. All current source paths/hashes match the new receipt;
  production source line endings are CRLF as required by `.gitattributes`.
- Repository hygiene, Roslyn syntax, diagnostics and UI/settings/world suites
  passed, including four UI, three world and one settings negative controls.
  `test-harmony-patches.ps1`: 39/39 target/parameter binding checks passed.
- `package-release.ps1 -Version 0.2.0` produced one `RimTouch/` archive with 10
  verified entries: About (including unchanged preview and Workshop ID),
  Languages, DLL, LICENSE, ASSETS.md and CREDITS.md.
- DLL SHA256:
  `9078e6cf2e2ff9aef5416af175e7b24400a3b860201d13d8229fabebc9538545`.
- `.build/releases/RimTouch-v0.2.0.zip` SHA256:
  `cee21c9d982b15fa16f6829b48bfc5628d0db04e4ee7875c3b2b78f1dd31a720`.
- All ZIP payload hashes match the current repository/build files and the single
  canonical Mod at `.build/local/RimTouch/`. The DLL matches the accepted fix's
  previously delivered DLL. No install, game launch, git tag or publication was
  performed during this preparation.
- Users accepted the affected camera/slider/UI/tap fixes. Final tap feedback:
  "已测试，目前涉及到的点都改好了。没有明显的bug。" Detailed chronology and limits:
  `docs/ui-touch-findings.md`. This is not a per-device/per-mod compatibility matrix.
- Tag workflow suite parity and embedded PowerShell syntax were checked locally.
  Tag annotation/main ancestry and remote release-tag CI require the actual
  annotated tag on `main`; they have not run for 0.2.0 at this preparation stage.
- Compilation/stub suites do not prove native event ordering for every renderer,
  Linux, scaling or mod combination. Fully custom UI with no event/control claim
  remains a boundary; general drag/hold/multitouch control acquisition is planned.
  `Source-Snapshot.json` is historical and is not a statement that today's source
  still matches the original snapshot.

## Historical original snapshot build (retained evidence)

The selected 17 production C# files compiled successfully against RimWorld 1.7.4990 rev1069 and the matching installed Harmony assembly. The portable Build17.ps1 helper was used.

- Compiler diagnostics: 0.
- Product SHA256: `226EB965B2843842CB9190D211F3D8E851B55265D0A31ADC887E8E482307C96F`.
- Every published original C# file is byte-identical to its selected working source.
- This pass did not launch RimWorld, rerun native gameplay tests, change installed DLLs or test legacy game versions.
- A successful API build does not prove renderer, touchscreen, full-mod-list or long-term save compatibility.
