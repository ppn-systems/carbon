#!/usr/bin/env bash
# Renders every scenario in scenarios.txt with the desktop client (headless via Xvfb when no display is
# available) and writes <out>/<name>.png plus <out>/contact_sheet.png for visual review.
#
# Usage: tools/screenshots/capture.sh [out_dir] [scenario_filter_regex]
# Env:   CARBON_SEED (default 1337), WIDTH/HEIGHT (default 1280x720).
# Needs: dotnet, python3 with Pillow, and on Linux: xvfb-run + libglfw3 + Mesa (software GL is fine).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$ROOT/screenshots/review}"
FILTER="${2:-.}"
WIDTH="${WIDTH:-1280}"
HEIGHT="${HEIGHT:-720}"
export PATH="$HOME/.dotnet:$PATH"

# Without a display, re-run this whole script once inside a virtual X server.
if [[ -z "${DISPLAY:-}" ]]; then
  exec xvfb-run -a -s "-screen 0 ${WIDTH}x${HEIGHT}x24" "$0" "$@"
fi
mkdir -p "$OUT"

dotnet build "$ROOT/desktop/Carbon.Desktop/Carbon.Desktop.csproj" -c Release -nologo -v q
DLL="$(find "$ROOT/artifacts/bin/Carbon.Desktop" -path "*release*" -name Carbon.Desktop.dll | head -n 1)"


while IFS='|' read -r name vars; do
  name="$(echo "$name" | xargs)"
  [[ -z "$name" || "$name" == \#* ]] && continue
  [[ "$name" =~ $FILTER ]] || continue
  echo "[capture] $name ($vars)"
  bmp="$OUT/$name.bmp"
  rm -f "$bmp"
  # shellcheck disable=SC2086
  env CARBON_AUTO_EXIT=1 CARBON_SEED="${CARBON_SEED:-1337}" CARBON_SCREENSHOT_PATH="$bmp" LIBGL_ALWAYS_SOFTWARE=1 $vars \
    timeout 180 dotnet "$DLL" > "$OUT/$name.log" 2>&1 || echo "  (exit $?; see $OUT/$name.log)"
  if [[ -f "$bmp" ]]; then
    python3 -I -c "import sys; from PIL import Image; Image.open(sys.argv[1]).save(sys.argv[2])" "$bmp" "$OUT/$name.png" && rm -f "$bmp"
  else
    echo "  no capture produced"
  fi
done < "$ROOT/tools/screenshots/scenarios.txt"

python3 -I "$ROOT/tools/screenshots/contact_sheet.py" "$OUT"
echo "[capture] done -> $OUT"
