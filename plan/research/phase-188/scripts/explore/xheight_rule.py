"""Direction F: the designer's x-height / cap rounding at the INTEGER ppem FreeType renders bytecode fonts at,
vs Native's rule floor(x + 2/3) at the fractional ppem, vs round-to-nearest. Also cvt membership of measured values."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from fontTools.ttLib import TTFont

def rows_of(fnt, ch):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    r0, r1 = ink_rows(g, 128)
    return 0 if r0 is None else r1 - r0 + 1

out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

agree = {"nearest": 0, "floor+2/3": 0, "floor+0.5@int": 0, "n": 0}
for font in ["verdana", "tahoma", "georgia", "arial"]:
    tt = TTFont(FONT_FILES[font], lazy=True); upem = tt["head"].unitsPerEm; os2 = tt["OS/2"]; cell = os2.usWinAscent + os2.usWinDescent
    cmap = tt.getBestCmap(); glyf = tt["glyf"]; xh_u = glyf[cmap[ord("x")]].yMax; cap_u = glyf[cmap[ord("H")]].yMax
    cvt = list(tt["cvt "].values) if "cvt " in tt else []
    P(f"\n== {font}: xh={xh_u}u cap={cap_u}u  cvt has xh? {xh_u in cvt}  cap? {cap_u in cvt}  (cvt n={len(cvt)}; os2 sxHeight={getattr(os2,'sxHeight',None)} sCapHeight={getattr(os2,'sCapHeight',None)})")
    P(f"{'sz':>3} {'ppem':>6} {'ippem':>5} | {'xh@int':>6} {'FT':>3} {'near':>4} {'f+2/3':>5} {'ours@frac':>9} {'NN':>3} | {'cap@int':>7} {'FT':>3} {'near':>4} {'NN':>3}")
    for size in [7, 8, 9, 10, 11, 12, 13, 14, 16, 20]:
        ppem = size * upem / cell; ip = round(ppem)
        ft = load(font, size, "ft", "none"); nn = load(font, size, "nn", "none")
        xi = xh_u * ip / upem; xf = xh_u * ppem / upem; ci = cap_u * ip / upem
        ftx, nnx = rows_of(ft, "x"), rows_of(nn, "x"); ftc, nnc = rows_of(ft, "H"), rows_of(nn, "H")
        near = round(xi); f23 = int(xi + 2/3); oursfrac = int(xf + 2/3)
        agree["n"] += 1; agree["nearest"] += near == ftx; agree["floor+2/3"] += f23 == ftx; agree["floor+0.5@int"] += int(xi + 0.5) == ftx
        P(f"{size:3d} {ppem:6.2f} {ip:5d} | {xi:6.2f} {ftx:3d} {near:4d} {f23:5d} {oursfrac:9d} {nnx:3d} | {ci:7.2f} {ftc:3d} {round(ci):4d} {nnc:3d}")
P(f"\nx-height rule agreement with the designer (FT mono) over {agree['n']} cells: nearest@int={agree['nearest']}  floor(x+2/3)@int={agree['floor+2/3']}")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "xheight_rule.txt"), "w").write("\n".join(out))
