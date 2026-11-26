"""Convert world captures (BMP) to PNG and lay them out on a labelled contact sheet.

Usage: python3 -I contact_sheet.py <out_dir> [scenarios.txt]

Every <name>.bmp in out_dir becomes <name>.png (the BMP is removed). The sheet, contact_sheet.png, shows the
captures in scenario-file order (any other PNGs follow alphabetically), each with its name underneath.
"""

import os
import sys

from PIL import Image, ImageDraw, ImageFont

THUMB_W, THUMB_H = 480, 270
LABEL_H = 26
PAD = 8
COLUMNS = 4
SHEET_NAME = "contact_sheet.png"


def scenario_order(path):
    names = []
    if path and os.path.isfile(path):
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.split("#", 1)[0].strip()
                if line:
                    names.append(line.split("|", 1)[0].strip())
    return names


def load_font(size):
    for candidate in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default(size=size)


def main(argv):
    if len(argv) < 2:
        print(__doc__, file=sys.stderr)
        return 2

    out_dir = argv[1]
    order = scenario_order(argv[2] if len(argv) > 2 else None)

    for entry in sorted(os.listdir(out_dir)):
        if entry.lower().endswith(".bmp"):
            src = os.path.join(out_dir, entry)
            with Image.open(src) as im:
                im.convert("RGB").save(os.path.join(out_dir, entry[:-4] + ".png"), optimize=True)
            os.remove(src)

    pngs = {e[:-4]: os.path.join(out_dir, e) for e in os.listdir(out_dir) if e.endswith(".png") and e != SHEET_NAME}
    names = [n for n in order if n in pngs] + sorted(n for n in pngs if n not in order)
    if not names:
        print("contact_sheet: no captures found", file=sys.stderr)
        return 1

    rows = (len(names) + COLUMNS - 1) // COLUMNS
    cols = min(COLUMNS, len(names))
    sheet = Image.new("RGB", (PAD + cols * (THUMB_W + PAD), PAD + rows * (THUMB_H + LABEL_H + PAD)), (18, 20, 26))
    draw = ImageDraw.Draw(sheet)
    font = load_font(16)
    for i, name in enumerate(names):
        x = PAD + (i % COLUMNS) * (THUMB_W + PAD)
        y = PAD + (i // COLUMNS) * (THUMB_H + LABEL_H + PAD)
        with Image.open(pngs[name]) as im:
            sheet.paste(im.convert("RGB").resize((THUMB_W, THUMB_H), Image.LANCZOS), (x, y))
        draw.text((x + 4, y + THUMB_H + 4), name, fill=(230, 232, 238), font=font)

    sheet_path = os.path.join(out_dir, SHEET_NAME)
    sheet.save(sheet_path, optimize=True)
    print(f"contact_sheet: {len(names)} capture(s) -> {sheet_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
