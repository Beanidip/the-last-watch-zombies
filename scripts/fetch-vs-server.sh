#!/usr/bin/env bash
# Download the official Vintage Story dedicated server (Linux x64) and extract it
# into DEST, for use ONLY as compile-time references (VintageStoryPath).
#
# The server archive is proprietary Anura Games software. It is downloaded from
# the official CDN at build time and must never be committed to this repository
# or redistributed. The mod's .csproj marks every game reference Private=false,
# so no game assembly is copied into bin/ or the packaged mod ZIP.
#
# Usage: scripts/fetch-vs-server.sh [VERSION] [DEST]
#   VERSION defaults to 1.22.7, DEST defaults to ./vs-server (git-ignored).
set -euo pipefail

VERSION="${1:-1.22.7}"
DEST="${2:-vs-server}"
URL="https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_${VERSION}.tar.gz"

# Known-good archive hashes. Add a line when the target game version changes.
declare -A SHA256=(
  ["1.22.7"]="a7d4a520604590a96de812ee6c6baf4404d43d86cf1ec4fbac1b3a580b52c847"
)

if [[ -f "$DEST/VintagestoryAPI.dll" && -f "$DEST/Mods/VSSurvivalMod.dll" ]]; then
  echo "Vintage Story $VERSION references already present in $DEST"
  exit 0
fi

mkdir -p "$DEST"
archive="$(mktemp -t vs_server_XXXXXX.tar.gz)"
trap 'rm -f "$archive"' EXIT

echo "Downloading $URL"
curl --fail --location --silent --show-error --retry 3 -o "$archive" "$URL"

expected="${SHA256[$VERSION]:-}"
actual="$(sha256sum "$archive" | cut -d' ' -f1)"
if [[ -n "$expected" && "$actual" != "$expected" ]]; then
  echo "SHA-256 mismatch for $VERSION: expected $expected, got $actual" >&2
  exit 1
fi
[[ -z "$expected" ]] && echo "WARNING: no pinned hash for $VERSION (got $actual)" >&2

tar -xzf "$archive" -C "$DEST"
for f in VintagestoryAPI.dll VintagestoryLib.dll Mods/VSEssentials.dll Mods/VSSurvivalMod.dll Lib/0Harmony.dll Lib/Newtonsoft.Json.dll Lib/protobuf-net.dll; do
  [[ -f "$DEST/$f" ]] || { echo "Missing $f after extraction" >&2; exit 1; }
done
echo "Vintage Story $VERSION references extracted to $DEST"
