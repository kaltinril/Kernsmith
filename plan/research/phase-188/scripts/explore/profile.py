"""Direction G (broken rings): coverage of round strokes. For bowl glyphs, per row the max coverage in the left half
and the right half of the glyph; the ring's weakest point is the min over the middle rows. Also column sums."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *

out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

def weakest(g):
    h, w = g.shape
    if w < 2 or h < 3: return 0, 0
    mid = w // 2
    rows = range(1, h - 1)
    left = [int(g[r, :mid].max()) for r in rows]; right = [int(g[r, mid:].max()) for r in rows]
    # top and bottom rows (the horizontal parts of the ring)
    top = int(g[0].max()); bot = int(g[-1].max())
    return min(left), min(right), top, bot

for font in ["roboto", "verdana", "georgia"]:
    for size in [9, 10, 11, 12]:
        for ch in "oecsbdpqg0":
            line = f"{font:8s} {size:2d}px '{ch}' weakest(left,right,top,bottom) coverage: "
            for v in ["ft", "nn", "nd"]:
                try:
                    fnt = load(font, size, v, "grayscale")
                except FileNotFoundError:
                    continue
                g, c = fnt.glyph(ch)
                if g is None: continue
                wl, wr, t, b = weakest(g)
                line += f" {v}={wl:3d},{wr:3d},{t:3d},{b:3d}  "
            P(line)
    # column profile of 'o' at 10, 12
    for size in [10, 12]:
        for v in ["ft", "nn", "nd"]:
            fnt = load(font, size, v, "grayscale"); g, c = fnt.glyph("o")
            if g is None: continue
            P(f"{font} {size}px 'o' {v}: shape={g.shape} colsum={[int(x) for x in (g.sum(axis=0)/255*100).round()]} rows:")
            for r in g: P("      " + " ".join(f"{int(x):3d}" for x in r))
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "profile.txt"), "w").write("\n".join(out))
