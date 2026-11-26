#!/usr/bin/env bash
# CI smoke test for the screenshot tool: capture one scenario and check the image is real (not missing, not
# a single flat colour, i.e. the world actually rendered).
#
#   tools/screenshots/smoke.sh [scenario]     (default: lush_plains)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCENARIO="${1:-lush_plains}"
OUT="$(mktemp -d "${TMPDIR:-/tmp}/carbon-smoke.XXXXXX")"
trap 'rm -rf "$OUT"' EXIT

"$ROOT/tools/screenshots/capture.sh" "$OUT" "^${SCENARIO}\$"

python3 -I - "$OUT/$SCENARIO.png" <<'EOF'
import sys
from PIL import Image, ImageStat

path = sys.argv[1]
with Image.open(path) as im:
    stat = ImageStat.Stat(im.convert("RGB"))
    spread = max(stat.stddev)
    colours = len(im.convert("RGB").getcolors(maxcolors=1 << 20) or [])
print(f"smoke: {path}: {im.size[0]}x{im.size[1]}, stddev {spread:.1f}, {colours} colours")
if im.size != (1280, 720) or spread < 8 or colours < 256:
    print("smoke: FAILED (capture looks empty)", file=sys.stderr)
    sys.exit(1)
print("smoke: OK")
EOF
