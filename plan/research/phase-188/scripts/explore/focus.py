import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from PIL import Image, ImageDraw
here = os.path.dirname(os.path.abspath(__file__))
def sheet(font, aa, glyphs, sizes, variants, name, sc=10):
    cw, chh = 12*sc, 18*sc
    W = 90 + len(glyphs)*(len(variants)*cw+12); H = 26 + len(sizes)*(chh+14)
    img = Image.new("RGB", (W, H), (20,20,20)); dr = ImageDraw.Draw(img)
    x = 90
    for ch in glyphs:
        dr.text((x+2, 4), f"'{ch}': " + " | ".join(variants), fill=(255,200,0)); x += len(variants)*cw+12
    y = 26
    for size in sizes:
        dr.text((4, y+4), f"{font} {size}px\n{aa}", fill=(255,200,0)); x = 90
        for ch in glyphs:
            for v in variants:
                try: can = load(font, size, v, aa).place(ch, canvas_w=12, canvas_h=18)
                except FileNotFoundError: can = None
                if can is not None:
                    img.paste(Image.fromarray(can).resize((12*sc, 18*sc), Image.NEAREST).convert("RGB"), (x, y))
                dr.rectangle([x, y, x+cw-1, y+chh-1], outline=(70,70,70)); x += cw
            x += 12
        y += chh+14
    img.save(os.path.join(here, "crops", name)); print(name, img.size)
sheet("roboto", "grayscale", "eas8g", [8,9,10], ["ft","nn","nd"], "focus_roboto_gray_8-10.png")
sheet("verdana", "none", "easg", [9,10,11], ["ft","nn","nd"], "focus_verdana_mono_9-11.png")
sheet("roboto", "none", "easg8", [9,10,12], ["ft","nn","nd"], "focus_roboto_mono_9-12.png")
sheet("georgia", "none", "eagsR", [10,11,12], ["ft","nn","nd"], "focus_georgia_mono_10-12.png")
