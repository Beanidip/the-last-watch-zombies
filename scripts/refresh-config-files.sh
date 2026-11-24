#!/usr/bin/env bash
# Regenerate every configuration artifact from src/Config.cs:
#   DEFAULT-thelastwatchzombiesconfig{main,advanced}.json  (example configs)
#   CONFIG-REFERENCE.md                                     (shipped in the mod ZIP)
#   docs/CONFIGURATION.md                                   (developer reference)
# Usage: scripts/refresh-config-files.sh [VintageStoryPath]   (default ./vs-server)
# With --check, regenerate into a temp dir and fail if anything differs.
set -euo pipefail
cd "$(dirname "$0")/.."
CHECK=0; VS=""
for arg in "$@"; do [[ "$arg" == "--check" ]] && CHECK=1 || VS="$arg"; done
VS="${VS:-$PWD/vs-server}"
OUT="$(mktemp -d)"; trap 'rm -rf "$OUT"' EXIT
dotnet run --project tools/ConfigDump -c Release "-p:VintageStoryPath=$VS" -- "$OUT/dump" . > "$OUT/report.txt"
tail -1 "$OUT/report.txt"
mkdir -p "$OUT/gen/docs"
cp "$OUT/dump/thelastwatchzombiesconfigmain.json" "$OUT/gen/DEFAULT-thelastwatchzombiesconfigmain.json"
cp "$OUT/dump/thelastwatchzombiesconfigadvanced.json" "$OUT/gen/DEFAULT-thelastwatchzombiesconfigadvanced.json"
python3 scripts/gen-config-doc.py "$OUT/dump" --reference > "$OUT/gen/CONFIG-REFERENCE.md"
python3 scripts/gen-config-doc.py "$OUT/dump" > "$OUT/gen/docs/CONFIGURATION.md"
FILES=(DEFAULT-thelastwatchzombiesconfigmain.json DEFAULT-thelastwatchzombiesconfigadvanced.json CONFIG-REFERENCE.md docs/CONFIGURATION.md)
if [[ $CHECK == 1 ]]; then
  stale=0
  for f in "${FILES[@]}"; do
    if ! diff -q <(tr -d '\r' < "$f") <(tr -d '\r' < "$OUT/gen/$f") > /dev/null; then echo "STALE: $f"; stale=1; fi
  done
  [[ $stale == 0 ]] && echo "Configuration artifacts are up to date." || { echo "Run scripts/refresh-config-files.sh and commit the result."; exit 1; }
else
  for f in "${FILES[@]}"; do cp "$OUT/gen/$f" "$f"; done
  echo "Regenerated: ${FILES[*]}"
fi
