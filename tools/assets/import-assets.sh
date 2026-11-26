#!/usr/bin/env bash
# Imports the third-party art packs (X.zip) into resources/thirdparty/, which is git-ignored, as REFERENCE
# material (poses, animation timing, palettes) for authoring resources/art/*.cvg.json vector art.
#
# The game itself never loads these raster files (ADR-005: all art is declarative .cvg.json). Their licenses
# also forbid redistributing the raw files (the effects pack forbids bundling with a game engine) and this
# repository is public, so they must never be committed.
#
# Usage: tools/assets/import-assets.sh /path/to/X.zip
set -euo pipefail

ZIP="${1:?usage: import-assets.sh /path/to/X.zip}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEST="$ROOT/resources/thirdparty"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

unzip -q "$ZIP" -d "$TMP"
SRC="$(find "$TMP" -maxdepth 2 -type d -name X | head -n 1)"
SRC="${SRC:-$TMP}"

copy() { # copy <relative source dir under the pack root> <destination subdir>
  local from="$SRC/$1" to="$DEST/$2"
  if [[ -d "$from" ]]; then mkdir -p "$to"; cp -R "$from"/. "$to"/; echo "  + $2"; else echo "  - missing: $1"; fi
}

echo "[assets] importing into $DEST"
rm -rf "$DEST"
copy "FreeCharactersAnimationsAssetPack/FreeCharactersAnimationsAssetPack/SpriteSheets(96x96)" "characters96"
copy "Tiny RPG Character Asset Pack 01 v2.0 -Free Soldier&Orc/Tiny RPG Character Asset Pack 01 v2.0 -Free Soldier&Orc/Characters(100x100 split)" "tinyrpg01"
copy "Tiny RPG Character Asset Pack 01 v2.0 -Free Soldier&Orc/Tiny RPG Character Asset Pack 01 v2.0 -Free Soldier&Orc/Arrow(Projectile)" "tinyrpg01/Arrow"
copy "Tiny RPG Character Asset Pack 02 v1.01-Free Demon_A&Blood Monster_A/Tiny RPG Character Asset Pack 02 -Free Demon_A&Blood Monster_A/Characters(100x100 split)" "tinyrpg02"
copy "Super Pixel Effects Gigapack (Free Version) v3.3.0/Super Pixel Effects Gigapack (Free Version)/spritesheet" "effects"
copy "Pixel Crawler - Free Pack (Exterior 0.1)/Pixel Crawler - Free Pack" "pixelcrawler"
copy "Ghostpixxells_pixelfood" "food"
copy "items" "items"
copy "panels" "panels"
find "$DEST" -name "*.aseprite" -delete
echo "[assets] done ($(find "$DEST" -type f | wc -l) files)"
