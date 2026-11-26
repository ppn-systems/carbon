"""Tiles every <name>.png in a directory into contact_sheet.png with labels, for side-by-side review."""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

out = Path(sys.argv[1])
shots = sorted(p for p in out.glob("*.png") if p.name != "contact_sheet.png")
if not shots:
    sys.exit("no screenshots found")

thumb_w, thumb_h, cols = 480, 270, 3
rows = (len(shots) + cols - 1) // cols
sheet = Image.new("RGB", (cols * thumb_w, rows * (thumb_h + 22)), (20, 20, 24))
draw = ImageDraw.Draw(sheet)
for i, path in enumerate(shots):
    x, y = (i % cols) * thumb_w, (i // cols) * (thumb_h + 22)
    sheet.paste(Image.open(path).convert("RGB").resize((thumb_w, thumb_h)), (x, y + 22))
    draw.text((x + 6, y + 5), path.stem, fill=(235, 235, 235))
sheet.save(out / "contact_sheet.png")
print(f"contact sheet: {out / 'contact_sheet.png'} ({len(shots)} shots)")
