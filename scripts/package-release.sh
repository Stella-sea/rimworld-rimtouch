#!/bin/bash
# Package a tested RimTouch build as a versioned GitHub Release asset.
# Usage: scripts/package-release.sh <version> [staging-dir]
#   version: e.g. 0.1.0-test.12  (no leading "v"; the tag is v<version>)
#   staging-dir: a directory holding About/, Assemblies/RimTouch.dll
#     and Languages/ for this build.
# Output: RimTouch-fix-test<N>.zip in the current directory, containing
#   a single root folder RimTouch/ so it extracts straight into
#   RimWorld/Mods/. Prints the SHA256 for the release notes.
# The DLL hash is checked against the fresh local build in
#   fork-rimtouch/.build/1.7/RimTouch.dll, and <modVersion> in
#   About/About.xml is checked against <version> — if either differs,
#   stop: the zip must be the build from the tagged commit, not a stale DLL
#   or a mismatched version stamp.
set -euo pipefail

VERSION="${1:-}"
STAGING="${2:-$HOME/workspace/research/rimtouch/staging-0.1.0-test.12}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
N="${VERSION##*-test.}"
OUT="$PWD/RimTouch-fix-test$N.zip"

[ -n "$VERSION" ] || { echo "usage: $0 <version> [staging-dir]" >&2; exit 2; }
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+-test\.[0-9]+$ ]] || { echo "version must look like 0.1.0-test.12" >&2; exit 2; }
[ -f "$STAGING/About/About.xml" ] || { echo "staging dir missing About/About.xml: $STAGING" >&2; exit 1; }
[ -f "$STAGING/Assemblies/RimTouch.dll" ] || { echo "staging dir missing Assemblies/RimTouch.dll" >&2; exit 1; }
[ -f "$ROOT/.build/1.7/RimTouch.dll" ] || { echo "fresh build missing: run build17.sh first" >&2; exit 1; }

BUILT_HASH="$(sha256sum "$ROOT/.build/1.7/RimTouch.dll" | cut -d' ' -f1)"
STAGED_HASH="$(sha256sum "$STAGING/Assemblies/RimTouch.dll" | cut -d' ' -f1)"
[ "$BUILT_HASH" = "$STAGED_HASH" ] || {
  echo "DLL mismatch: staged $STAGED_HASH != fresh build $BUILT_HASH" >&2
  echo "Rebuild, restage, then repackage." >&2; exit 1; }

grep -q "<modVersion>$VERSION</modVersion>" "$STAGING/About/About.xml" || { echo "modVersion in About.xml does not match $VERSION" >&2; exit 1; }
grep -q '<packageId>cinder.rimtouch17.private</packageId>' "$STAGING/About/About.xml" || { echo "packageId check failed" >&2; exit 1; }
grep -q '<li>1.7</li>' "$STAGING/About/About.xml" || { echo "supportedVersions check failed" >&2; exit 1; }

rm -rf "$PWD/.package-tmp" && mkdir -p "$PWD/.package-tmp/RimTouch"
cp -r "$STAGING/About" "$STAGING/Assemblies" "$STAGING/Languages" "$PWD/.package-tmp/RimTouch/"
rm -f "$OUT"
( cd "$PWD/.package-tmp" && zip -qr "$OUT" RimTouch )
rm -rf "$PWD/.package-tmp"

echo "asset: $OUT"
unzip -l "$OUT"
echo "SHA256: $(sha256sum "$OUT" | cut -d' ' -f1)"
echo "DLL SHA256: $BUILT_HASH"
