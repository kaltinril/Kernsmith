"""Direction A: which 'the font already knows' tables do fixtures + system fonts carry?
Run with: python -I survey_tables.py
"""
import glob, os, sys, json
from fontTools.ttLib import TTFont

WT = os.environ.get("KERNSMITH_ROOT", ".")
fixtures = glob.glob(WT + "/tests/KernSmith.Tests/Fixtures/*.ttf") + glob.glob(WT + "/tests/KernSmith.Tests/Fixtures/*.otf")
system = [p for p in glob.glob("C:/Windows/Fonts/*.ttf")] + glob.glob("C:/Windows/Fonts/*.otf") + glob.glob("C:/Windows/Fonts/*.ttc")
rows = []
def one(path, cls):
    try:
        f = TTFont(path, fontNumber=0, lazy=True)
    except Exception as e:
        return
    tags = set(f.keys())
    r = dict(font=os.path.basename(path), cls=cls)
    for t in ["hdmx", "VDMX", "LTSH", "gasp", "cvt ", "fpgm", "prep", "CFF ", "CFF2", "glyf"]:
        r[t.strip()] = t in tags
    r["instr"] = False
    if "glyf" in tags:
        try:
            g = f["glyf"]
            # sample a few glyphs for instructions
            names = list(g.keys())[:200]
            r["instr"] = any(getattr(g[n], "program", None) and len(g[n].program.getBytecode()) > 0 for n in names if hasattr(g[n], "program") and not g[n].isComposite())
        except Exception:
            pass
    if "gasp" in tags:
        try:
            r["gasp_ranges"] = {str(k): int(v) for k, v in sorted(f["gasp"].gaspRange.items())}
        except Exception as e:
            r["gasp_ranges"] = str(e)
    if "hdmx" in tags:
        try:
            r["hdmx_ppems"] = sorted(int(k) for k in f["hdmx"].hdmx.keys())
        except Exception as e:
            r["hdmx_ppems"] = str(e)
    if "cvt " in tags:
        try:
            r["cvt_n"] = len(f["cvt "].values)
        except Exception:
            pass
    if "CFF " in tags:
        try:
            td = f["CFF "].cff.topDictIndex[0]
            pd = td.Private if hasattr(td, "Private") else None
            if pd is not None:
                r["cff_private"] = {k: getattr(pd, k) for k in ["StdHW", "StdVW", "StemSnapH", "StemSnapV", "BlueValues", "OtherBlues", "BlueScale", "BlueShift", "BlueFuzz"] if hasattr(pd, k)}
        except Exception as e:
            r["cff_private"] = str(e)
    rows.append(r)

for p in fixtures: one(p, "fixture")
for p in system: one(p, "system")

tags = ["hdmx", "VDMX", "LTSH", "gasp", "cvt", "fpgm", "prep", "instr", "CFF", "CFF2"]
for cls in ["fixture", "system"]:
    sub = [r for r in rows if r["cls"] == cls]
    print(f"\n== {cls}: {len(sub)} fonts ==")
    for t in tags:
        n = sum(1 for r in sub if r.get(t))
        print(f"  {t:6s} {n:4d} ({100*n/len(sub):.0f}%)")
print("\n== per font (fixtures + named system fonts) ==")
named = {"arial.ttf", "verdana.ttf", "tahoma.ttf", "georgia.ttf", "consola.ttf", "segoeui.ttf", "times.ttf", "calibri.ttf", "trebuc.ttf", "cour.ttf"}
for r in rows:
    if r["cls"] == "fixture" or r["font"].lower() in named:
        flags = " ".join(t for t in tags if r.get(t))
        extra = ""
        if "gasp_ranges" in r: extra += f" gasp={r['gasp_ranges']}"
        if "hdmx_ppems" in r: extra += f" hdmx={r['hdmx_ppems'][:3]}..{r['hdmx_ppems'][-1:]}(n={len(r['hdmx_ppems'])})" if isinstance(r['hdmx_ppems'], list) else f" hdmx={r['hdmx_ppems']}"
        if "cvt_n" in r: extra += f" cvt_n={r['cvt_n']}"
        if "cff_private" in r: extra += f" cffpriv={r['cff_private']}"
        print(f"  {r['font']:34s} [{flags}]{extra}")
json.dump(rows, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "survey_tables.json"), "w"), indent=1)
