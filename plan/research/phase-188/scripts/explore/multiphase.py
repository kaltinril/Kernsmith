"""Direction D: spacing-evenness gain of N horizontal sub-pixel phases per glyph (vs 1) at 10-12 px.
Model: ideal (unrounded) pen positions from font units at the render ppem; a bigram's ideal ink gap =
(adv_a - inkmax_a) + inkmin_b. Rendered gap under a policy = (rounded pen advance) + (ink edge positions quantised
to the phase grid). Error = rendered - ideal, in px. Policies:
  1-phase round:    pen advances to round(adv) each glyph, glyph drawn at its integer-rounded ink offset.
  1-phase diffuse:  pen position kept exactly, glyph drawn at round(pen) (error-diffused; runtime, known string).
  N-phase:          pen kept exactly, glyph drawn at nearest 1/N (atlas holds N variants).
Also the line-length drift of policy 1 vs ideal over the pangram.
"""
import sys, os, math, itertools
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from fontTools.ttLib import TTFont
from fontTools.pens.boundsPen import BoundsPen

TEXT = "The quick brown fox jumps over the lazy dog. Hamburgefonstiv 0123456789 minimum illegible"
out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

def ink_edges(tt, cmap, gs, ch, scale):
    gn = cmap.get(ord(ch))
    if gn is None: return None
    bp = BoundsPen(gs); gs[gn].draw(bp)
    adv = tt["hmtx"].metrics[gn][0] * scale
    if bp.bounds is None: return (adv, None, None)
    xmin, _, xmax, _ = bp.bounds
    return (adv, xmin * scale, xmax * scale)

P(f"{'font':8s} {'px':>3} {'ppem':>6} | policy: RMS gap err / max / %|err|>=0.5  (bigrams in pangram)        | drift(px) 1-round")
for font in ["roboto", "verdana", "georgia", "arial"]:
    tt = TTFont(FONT_FILES[font], lazy=True); upem = tt["head"].unitsPerEm; os2 = tt["OS/2"]
    cell = os2.usWinAscent + os2.usWinDescent; cmap = tt.getBestCmap(); gs = tt.getGlyphSet()
    for size in [8, 10, 11, 12, 14, 16]:
        ppem = size * upem / cell; scale = ppem / upem
        glyphs = {ch: ink_edges(tt, cmap, gs, ch, scale) for ch in set(TEXT)}
        pairs = [(a, b) for a, b in zip(TEXT, TEXT[1:]) if glyphs[a] and glyphs[b] and glyphs[a][1] is not None and glyphs[b][1] is not None]
        ideal = [(glyphs[a][0] - glyphs[a][2]) + glyphs[b][1] for a, b in pairs]
        results = {}
        # policy 1-round: each glyph drawn at integer pen + round(xmin) ... pen advances by round(adv)
        def run(policy, N=1):
            errs = []; pen_exact = 0.0; pen_int = 0.0
            for i, (a, b) in enumerate(pairs):
                adv_a, xmin_a, xmax_a = glyphs[a]; adv_b, xmin_b, xmax_b = glyphs[b]
                if policy == "round":
                    # glyph a drawn at pen_int; its right ink at pen_int + xmax_a (bitmap: xoffset=floor(xmin), width covers xmax; ink edge quantised to pixel grid by rounding the outline origin to integer: ink right edge = pen_int + xmax_a)
                    right_a = pen_int + xmax_a
                    pen_int += round(adv_a + 1e-9)
                    left_b = pen_int + xmin_b
                elif policy == "diffuse":
                    right_a = round(pen_exact) + xmax_a
                    pen_exact += adv_a
                    left_b = round(pen_exact) + xmin_b
                else:  # N phases
                    q = lambda p: round(p * N) / N
                    right_a = q(pen_exact) + xmax_a
                    pen_exact += adv_a
                    left_b = q(pen_exact) + xmin_b
                errs.append((left_b - right_a) - ideal[i])
            return errs
        rows = []
        for name, pol, N in [("1-round", "round", 1), ("1-diffuse", "diffuse", 1), ("2-phase", "N", 2), ("4-phase", "N", 4)]:
            e = run(pol, N)
            rms = math.sqrt(sum(x * x for x in e) / len(e)); mx = max(abs(x) for x in e); big = 100 * sum(1 for x in e if abs(x) >= 0.5) / len(e)
            rows.append(f"{name}: {rms:.2f}/{mx:.2f}/{big:4.0f}%")
        # drift
        total_ideal = sum(glyphs[c][0] for c in TEXT if glyphs[c]); total_round = sum(round(glyphs[c][0] + 1e-9) for c in TEXT if glyphs[c])
        P(f"{font:8s} {size:3d} {ppem:6.2f} | " + "  ".join(rows) + f" | {total_round - total_ideal:+.1f} over {len(TEXT)} chars")

P("\nAtlas cost: N phases = N x glyph count (each phase its own rect); 2-phase doubles page area at a given size; "
  "a BMFont-compatible encoding: duplicate chars at private-use ids (U+E000+ (phase<<16|cp) or a 'kernsmith' line mapping), "
  "with the renderer picking the variant whose phase is nearest frac(pen). Kerning tables cannot select a phase (they only add an integer).")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "multiphase.txt"), "w").write("\n".join(out))
