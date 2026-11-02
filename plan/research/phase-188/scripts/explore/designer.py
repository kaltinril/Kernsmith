"""Direction F/C: designer-approved pixels (FreeType mono on bytecode fonts, GDI) vs Native normal: x-height, cap height,
stem width, counters, per ppem. Also what the designers did with fractional x-heights (round rule)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from fontTools.ttLib import TTFont

out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

def h_of(fnt, ch, thr=128):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    r0, r1 = ink_rows(g, thr)
    return 0 if r0 is None else r1 - r0 + 1

def stem_of(fnt, ch="l", thr=128):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    row = g[g.shape[0] // 2]
    cols = np.where(row >= thr)[0]
    return len(cols)

def stem_cov(fnt, ch="l"):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    return float(g[g.shape[0] // 2].sum() / 255)

def holes_of(fnt, ch, thr=128):
    g, c = fnt.glyph(ch)
    return -1 if g is None else holes(g >= thr)

def comps_of(fnt, ch, thr=128):
    g, c = fnt.glyph(ch)
    return -1 if g is None else components(g >= thr)

def w_of(fnt, ch, thr=128):
    g, c = fnt.glyph(ch)
    if g is None: return 0
    c0, c1 = ink_cols(g, thr)
    return 0 if c0 is None else c1 - c0 + 1

xh_rule = {}  # (font) -> list of (xh_frac_px, designer_px)
for font in ["verdana", "tahoma", "georgia", "arial", "roboto", "notosans"]:
    tt = TTFont(FONT_FILES[font], lazy=True)
    upem = tt["head"].unitsPerEm; os2 = tt["OS/2"]; cell = os2.usWinAscent + os2.usWinDescent
    cmap = tt.getBestCmap(); glyf = tt["glyf"]
    xh_u = glyf[cmap[ord("x")]].yMax; cap_u = glyf[cmap[ord("H")]].yMax
    # vertical stem of 'l' in units: width of the glyph's bbox (sans) is a fair proxy for l
    l = glyf[cmap[ord("l")]]; stem_u = l.xMax - l.xMin
    P(f"\n=== {font}: upem={upem} xh={xh_u}u cap={cap_u}u l-width={stem_u}u  (mono columns: ft=FreeType bytecode mono, gdi, nn=Native normal, nd=Native unhinted; gray: ftG=FreeType default gray, nnG)")
    P(f"{'sz':>3} {'ppem':>6} | {'xh@ppem':>7} {'xh ft gdi nn nd | ftG nnG':<26} | {'cap@ppem':>8} {'cap ft gdi nn nd':<18} | {'stem@ppem':>9} {'l ft gdi nn nd':<16} {'lcov ftG nnG ndG':<18} | {'e-holes ft gdi nn nd':<22} {'a-holes':<14} {'g-holes':<14} {'s-comps':<14} {'o-w ft gdi nn nd':<18} | {'8-holes':<12} {'B-holes':<12}")
    for size in [7, 8, 9, 10, 11, 12, 13, 14, 16, 20]:
        ppem = size * upem / cell
        try:
            ft = load(font, size, "ft", "none"); gdi = load(font, size, "gdi", "none"); nn = load(font, size, "nn", "none"); nd = load(font, size, "nd", "none")
            ftg = load(font, size, "ft", "grayscale"); nng = load(font, size, "nn", "grayscale"); ndg = load(font, size, "nd", "grayscale")
        except FileNotFoundError as e:
            P("missing", e); continue
        M = [ft, gdi, nn, nd]
        xh = [h_of(m, "x") for m in M]; xhg = [h_of(m, "x", 128) for m in [ftg, nng]]
        cap = [h_of(m, "H") for m in M]
        st = [stem_of(m) for m in M]; sc = [stem_cov(m) for m in [ftg, nng, ndg]]
        eh = [holes_of(m, "e") for m in M]; ah = [holes_of(m, "a") for m in M]; gh = [holes_of(m, "g") for m in M]
        sco = [comps_of(m, "s") for m in M]; ow = [w_of(m, "o") for m in M]
        h8 = [holes_of(m, "8") for m in M]; hB = [holes_of(m, "B") for m in M]
        xh_rule.setdefault(font, []).append((xh_u * ppem / upem, xh[0], xh[1], cap_u * ppem / upem, cap[0], cap[1]))
        P(f"{size:3d} {ppem:6.2f} | {xh_u*ppem/upem:7.2f} {' '.join(f'{v:2d}' for v in xh)} | {' '.join(f'{v:2d}' for v in xhg)}        | {cap_u*ppem/upem:8.2f} {' '.join(f'{v:2d}' for v in cap):<18} | {stem_u*ppem/upem:9.2f} {' '.join(f'{v:2d}' for v in st):<16} {' '.join(f'{v:4.2f}' for v in sc):<18} | {' '.join(f'{v:2d}' for v in eh):<22} {' '.join(f'{v:2d}' for v in ah):<14} {' '.join(f'{v:2d}' for v in gh):<14} {' '.join(f'{v:2d}' for v in sco):<14} {' '.join(f'{v:2d}' for v in ow):<18} | {' '.join(f'{v:2d}' for v in h8):<12} {' '.join(f'{v:2d}' for v in hB):<12}")

P("\n=== x-height rounding rule in designer pixels (ft mono bytecode): fractional x-height -> designer rows; residual = designer - floor ===")
for font, rows in xh_rule.items():
    s = "; ".join(f"{fx:.2f}->{dft}/{dgdi}" for fx, dft, dgdi, *_ in rows)
    P(f"{font:8s} xh: {s}")
    s = "; ".join(f"{cx:.2f}->{cft}/{cgdi}" for *_, cx, cft, cgdi in rows)
    P(f"{font:8s} cap: {s}")
# threshold estimate: smallest fractional part that rounded up, largest that rounded down
ups, downs = [], []
for font, rows in xh_rule.items():
    if font in ("roboto", "notosans"): continue  # autohinted on FreeType, not designer
    for fx, dft, dgdi, cx, cft, cgdi in rows:
        for v, d in ((fx, dft), (cx, cft)):
            frac = v - int(v)
            if d == int(v) + 1: ups.append(frac)
            elif d == int(v): downs.append(frac)
P(f"designer round-up fractions: min={min(ups):.2f} (n={len(ups)}), round-down fractions: max={max(downs):.2f} (n={len(downs)})")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "designer.txt"), "w").write("\n".join(out))
