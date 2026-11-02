"""Direction A: do hdmx advances match FreeType hinted / GDI / Native advances? And at what ppem does FreeType really render?"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fntlib import *
from fontTools.ttLib import TTFont

ASCII = [chr(c) for c in range(33, 127)]
out = []
def P(*a):
    s = " ".join(str(x) for x in a); print(s); out.append(s)

for font in ["arial", "georgia", "tahoma", "verdana", "roboto"]:
    tt = TTFont(FONT_FILES[font], lazy=True)
    upem = tt["head"].unitsPerEm
    flags = tt["head"].flags
    os2 = tt["OS/2"]; cell = os2.usWinAscent + os2.usWinDescent
    hmtx = tt["hmtx"].metrics
    cmap = tt.getBestCmap()
    hdmx = tt["hdmx"].hdmx if "hdmx" in tt else None
    capH = tt["glyf"][cmap[ord("H")]].yMax if "glyf" in tt else None
    P(f"\n=== {font}: upem={upem} winCell={cell} head.flags bit3 (integer ppem)={bool(flags & 8)} hdmx={'yes' if hdmx else 'no'} capH={capH}u")
    P(f"{'size':>4} {'ppem':>6} {'FTcapH':>6} {'@frac':>6} {'@int':>5} | adv matches of {len(ASCII)}: ft_none  ft_gray  gdi  | nn_none nn_gray nd_gray | lin@frac lin@int hdmx[r] hdmx[f]  | nn_none=hdmx nn_gray=hdmx")
    for size in [8, 9, 10, 11, 12, 13, 14, 16, 20]:
        ppem = size * upem / cell
        r, f = round(ppem), int(ppem)
        try:
            ftn = load(font, size, "ft", "none"); ftg = load(font, size, "ft", "grayscale")
            gdi = load(font, size, "gdi", "none")
            nnn = load(font, size, "nn", "none"); nng = load(font, size, "nn", "grayscale"); ndg = load(font, size, "nd", "grayscale")
        except FileNotFoundError as e:
            P("missing", e); continue
        # FreeType cap height in pixels (rows of H in ft_none)
        g, c = ftn.glyph("H"); fth = g.shape[0] if g is not None else 0
        expF = capH * ppem / upem; expI = capH * r / upem
        def adv(fnt, ch): return fnt.chars[ord(ch)]["xadvance"] if ord(ch) in fnt.chars else None
        def lin(ch, p): return round(hmtx[cmap[ord(ch)]][0] * p / upem + 1e-9)
        def hd(ch, p):
            if not hdmx or p not in hdmx: return None
            return hdmx[p][cmap[ord(ch)]]
        refs = {}
        cnt = {k: 0 for k in ["ft_none","ft_gray","gdi","nn_none","nn_gray","nd_gray","lin_frac","lin_int","hdmx_r","hdmx_f","nnn_hd","nng_hd"]}
        n_hd = 0
        for ch in ASCII:
            a_ftn = adv(ftn, ch)
            if a_ftn is None: continue
            vals = dict(ft_none=a_ftn, ft_gray=adv(ftg, ch), gdi=adv(gdi, ch), nn_none=adv(nnn, ch), nn_gray=adv(nng, ch), nd_gray=adv(ndg, ch),
                        lin_frac=lin(ch, ppem), lin_int=lin(ch, r), hdmx_r=hd(ch, r), hdmx_f=hd(ch, f))
            # reference for "matches": FreeType mono hinted advance (designer bytecode)
            for k in ["ft_gray","gdi","nn_none","nn_gray","nd_gray","lin_frac","lin_int","hdmx_r","hdmx_f"]:
                if vals[k] is not None and vals[k] == a_ftn: cnt[k] += 1
            cnt["ft_none"] += 1
            if vals["hdmx_r"] is not None:
                n_hd += 1
                if vals["nn_none"] == vals["hdmx_r"]: cnt["nnn_hd"] += 1
                if vals["nn_gray"] == vals["hdmx_r"]: cnt["nng_hd"] += 1
        hdinfo = f"{cnt['hdmx_r']:7d} {cnt['hdmx_f']:7d}" if hdmx and (r in hdmx or f in hdmx) else "      -       -"
        P(f"{size:4d} {ppem:6.2f} {fth:6d} {expF:6.2f} {expI:5.2f} | {cnt['ft_none']:7d} {cnt['ft_gray']:8d} {cnt['gdi']:4d} | {cnt['nn_none']:7d} {cnt['nn_gray']:7d} {cnt['nd_gray']:7d} | {cnt['lin_frac']:8d} {cnt['lin_int']:7d} {hdinfo} | {cnt['nnn_hd']:4d}/{n_hd:<4d} {cnt['nng_hd']:4d}/{n_hd:<4d}")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hdmx_compare.txt"), "w").write("\n".join(out))
