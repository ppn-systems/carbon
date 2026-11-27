#!/usr/bin/env bash
# Hero attack filmstrips: renders the offline art showcase (`Carbon.Desktop --showcase`, no server) under xvfb and
# converts its pages to PNG. hero_attacks.png has one row per attack, sampled through the swing (see
# docs/art/combat-animation.md). Run it on two commits to compare before/after.
#
#   tools/screenshots/filmstrip.sh [out_dir]          (default: artifacts/filmstrip)
#   SKIP_BUILD=1 tools/screenshots/filmstrip.sh /tmp/after
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="${1:-$ROOT/artifacts/filmstrip}"
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH"
fi

for tool in dotnet xvfb-run python3; do
    command -v "$tool" >/dev/null 2>&1 || { echo "filmstrip: '$tool' not found" >&2; exit 1; }
done

CLIENT_DLL="$ROOT/artifacts/bin/Carbon.Desktop/release/Carbon.Desktop.dll"
if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    dotnet build "$ROOT/desktop/Carbon.Desktop/Carbon.Desktop.csproj" -c Release -v q -nologo >/dev/null
fi

mkdir -p "$OUT_DIR"
(cd "$ROOT" && timeout "${CLIENT_TIMEOUT:-120}" xvfb-run -a -s "-screen 0 1280x720x24" dotnet "$CLIENT_DLL" --showcase "$OUT_DIR")

python3 -I - "$OUT_DIR" <<'PY'
import pathlib, sys
from PIL import Image
out = pathlib.Path(sys.argv[1])
for bmp in sorted(out.glob("*.bmp")):
    with Image.open(bmp) as im:
        im.convert("RGB").save(bmp.with_suffix(".png"))
    bmp.unlink()
print(f"filmstrip: {out / 'hero_attacks.png'}")
PY
