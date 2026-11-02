"""Direction C: crops of the problem glyphs, columns = variants, rows = sizes, x8 nearest."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from PIL import Image, ImageDraw

GLYPHS = "eagsg8BR%@c3"
SIZES = [7, 8, 9, 10, 11, 12]
here = os.path.dirname(os.path.abspath(__file__))
os.makedirs(os.path.join(here, "crops"), exist_ok=True)
S = 8
for font in ["roboto", "verdana", "georgia", "arial"]:
    for aa in ["grayscale", "none"]:
        variants = ["ft", "nn", "nd"] + (["gdi"] if aa == "none" else ["ftu"])
        cellw = 14; cellh = 22
        W = 40 + len(GLYPHS) * len(variants) * cellw * S // 8 * 8 // 8
        # layout: for each size row, for each glyph a group of len(variants) cells
        gw = cellw * S; gh = cellh * S
        img = Image.new("RGB", (60 + len(GLYPHS) * (len(variants) * gw // S * S // S * 1 + 8) * 1, 10), (20, 20, 20))
        # simpler: compute per cell 14x22 px glyph canvas scaled S
        cw = 14 * S // 2; chh = 24 * S // 2  # x4 effective to keep file size sane
        sc = S // 2
        W = 70 + len(GLYPHS) * (len(variants) * cw + 10)
        H = 30 + len(SIZES) * (chh + 14)
        img = Image.new("RGB", (W, H), (20, 20, 20)); dr = ImageDraw.Draw(img)
        x = 70
        for gi, ch in enumerate(GLYPHS):
            dr.text((x + 2, 4), f"{ch}: " + " ".join(variants), fill=(255, 200, 0))
            x += len(variants) * cw + 10
        y = 30
        for size in SIZES:
            dr.text((4, y + 4), f"{font} {size}px {aa}", fill=(255, 200, 0))
            x = 70
            for ch in GLYPHS:
                for v in variants:
                    try:
                        fnt = load(font, size, v, aa)
                        can = fnt.place(ch, canvas_w=14, canvas_h=24)
                    except FileNotFoundError:
                        can = None
                    if can is not None:
                        tile = Image.fromarray(can).resize((14 * sc, 24 * sc), Image.NEAREST).convert("RGB")
                        img.paste(tile, (x, y))
                    dr.rectangle([x, y, x + cw - 1, y + chh - 1], outline=(60, 60, 60))
                    x += cw
                x += 10
            y += chh + 14
        img.save(os.path.join(here, "crops", f"crops_{font}_{aa}.png"))
print("ok")
