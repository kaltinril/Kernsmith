"""Measure the integer-ppem PoC (ni) and + nearest x-height (nx) against FreeType mono (designer pixels / autohinter):
advance matches, x-height / cap rows, mono bit-identical glyphs, and gray SSIM-free proxies (holes, components)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from fontTools.ttLib import TTFont

ASCII = [chr(c) for c in range(33, 127)]
out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

def rows_of(fnt, ch):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    r0, r1 = ink_rows(g, 128); return 0 if r0 is None else r1 - r0 + 1

def identical(ref, other):
    """mono: glyphs whose ink bitmap (after cropping to ink) is identical and whose baseline-relative box matches."""
    n = 0
    for ch in ASCII:
        a, ca = ref.glyph(ch); b, cb = other.glyph(ch)
        if ca is None or cb is None: continue
        if a is None and b is None: n += 1; continue
        if a is None or b is None: continue
        if a.shape != b.shape: continue
        if ((a >= 128) != (b >= 128)).any(): continue
        if (ca["yoffset"] - ref.base_line) != (cb["yoffset"] - other.base_line) or ca["xoffset"] != cb["xoffset"]: continue
        n += 1
    return n

def holes_total(fnt):
    t = 0
    for ch in ASCII:
        g, c = fnt.glyph(ch)
        if g is not None: t += holes(g >= 128)
    return t

tot = {}
for font in ["verdana", "tahoma", "arial", "georgia", "roboto"]:
    tt = TTFont(FONT_FILES[font], lazy=True); upem = tt["head"].unitsPerEm; os2 = tt["OS/2"]; cell = os2.usWinAscent + os2.usWinDescent
    P(f"\n=== {font} — reference: FreeType mono (ft). adv = advances equal to ft of 94; xh/cap rows of x/H; ident = mono glyphs bit-identical to ft; holes = total enclosed counters over ASCII (ft first)")
    P(f"{'sz':>3} {'ppem':>6} | {'adv nn ni nx':<14} | {'xh ft nn ni nx':<16} {'cap ft nn ni nx':<16} | {'ident nn ni nx':<16} | {'holes ft nn ni nx':<20} | gray adv nn ni nx | gray holes ft nn ni nx")
    for size in [7, 8, 9, 10, 11, 12, 13, 14, 16, 20]:
        ppem = size * upem / cell
        try:
            ft = load(font, size, "ft", "none"); V = {v: load(font, size, v, "none") for v in ["nn", "ni", "nx"]}
            ftg = load(font, size, "ft", "grayscale"); G = {v: load(font, size, v, "grayscale") for v in ["nn", "ni", "nx"]}
        except FileNotFoundError as e:
            P("missing", e); continue
        def advm(ref, o): return sum(1 for ch in ASCII if ord(ch) in ref.chars and ord(ch) in o.chars and ref.chars[ord(ch)]["xadvance"] == o.chars[ord(ch)]["xadvance"])
        adv = [advm(ft, V[v]) for v in ["nn", "ni", "nx"]]; advg = [advm(ftg, G[v]) for v in ["nn", "ni", "nx"]]
        xh = [rows_of(ft, "x")] + [rows_of(V[v], "x") for v in ["nn", "ni", "nx"]]
        cap = [rows_of(ft, "H")] + [rows_of(V[v], "H") for v in ["nn", "ni", "nx"]]
        idn = [identical(ft, V[v]) for v in ["nn", "ni", "nx"]]
        hl = [holes_total(ft)] + [holes_total(V[v]) for v in ["nn", "ni", "nx"]]
        hlg = [holes_total(ftg)] + [holes_total(G[v]) for v in ["nn", "ni", "nx"]]
        for i, v in enumerate(["nn", "ni", "nx"]):
            t = tot.setdefault((font, v), {"adv": 0, "advg": 0, "xh": 0, "cap": 0, "ident": 0, "n": 0})
            t["adv"] += adv[i]; t["advg"] += advg[i]; t["xh"] += xh[i + 1] == xh[0]; t["cap"] += cap[i + 1] == cap[0]; t["ident"] += idn[i]; t["n"] += 1
        P(f"{size:3d} {ppem:6.2f} | {' '.join(f'{a:3d}' for a in adv):<14} | {' '.join(f'{a:2d}' for a in xh):<16} {' '.join(f'{a:2d}' for a in cap):<16} | {' '.join(f'{a:3d}' for a in idn):<16} | {' '.join(f'{a:3d}' for a in hl):<20} | {' '.join(f'{a:3d}' for a in advg):<17} | {' '.join(f'{a:3d}' for a in hlg)}")

P("\n=== totals over 10 sizes (94 glyphs each): mono advances equal to FT / gray advances equal to FT / sizes with x-height rows equal / cap rows equal / mono bit-identical glyphs ===")
for (font, v), t in tot.items():
    P(f"{font:8s} {v}: adv {t['adv']:4d}/940  advGray {t['advg']:4d}/940  xh {t['xh']:2d}/10  cap {t['cap']:2d}/10  ident {t['ident']:4d}/940")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "poc_measure.txt"), "w").write("\n".join(out))
