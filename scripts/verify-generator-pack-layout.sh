#!/usr/bin/env bash
# Verify that ZeroAlloc.Mediator.Generator.<version>.nupkg ships its DLLs under
# analyzers/dotnet/cs/ and not under lib/. A regression here means external
# consumers of the package silently get no source generator (the consuming
# project loads the DLL as a regular library reference instead of an analyzer).
#
# See: fix(mediator.generator)! ship analyzer DLL under analyzers/dotnet/cs/
#
# Usage:
#   scripts/verify-generator-pack-layout.sh <artifacts-dir>
#
# Exits non-zero if the layout is wrong.

set -euo pipefail

ARTIFACTS_DIR="${1:-./artifacts}"

if [[ ! -d "$ARTIFACTS_DIR" ]]; then
  echo "ERROR: artifacts directory not found: $ARTIFACTS_DIR" >&2
  exit 1
fi

# Locate the generator nupkg. There may be multiple version-suffixed candidates
# (e.g. CI runs after release-please) — pick the first match.
NUPKG=$(ls -1 "$ARTIFACTS_DIR"/ZeroAlloc.Mediator.Generator.*.nupkg 2>/dev/null | head -n 1 || true)

if [[ -z "$NUPKG" ]]; then
  echo "ERROR: no ZeroAlloc.Mediator.Generator.*.nupkg found in $ARTIFACTS_DIR" >&2
  exit 1
fi

echo "Inspecting: $NUPKG"

# A nupkg is a zip; list entries.
ENTRIES=$(unzip -Z1 "$NUPKG")

echo "--- nupkg entries ---"
echo "$ENTRIES"
echo "---------------------"

# 1. Must contain the generator DLL under analyzers/dotnet/cs/.
if ! grep -qE '^analyzers/dotnet/cs/ZeroAlloc\.Mediator\.Generator\.dll$' <<<"$ENTRIES"; then
  echo "FAIL: ZeroAlloc.Mediator.Generator.dll missing from analyzers/dotnet/cs/" >&2
  exit 1
fi

# 2. Must NOT contain any DLL under lib/ — that signals the bug returned.
if grep -qE '^lib/.*\.dll$' <<<"$ENTRIES"; then
  echo "FAIL: nupkg contains DLLs under lib/ — generator is being shipped as a regular library reference" >&2
  echo "Offending entries:" >&2
  grep -E '^lib/.*\.dll$' <<<"$ENTRIES" >&2
  exit 1
fi

echo "OK: ZeroAlloc.Mediator.Generator nupkg layout is correct."
