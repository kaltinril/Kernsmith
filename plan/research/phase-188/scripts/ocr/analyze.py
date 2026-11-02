"""Summarise results.json from ocr_bench.py into markdown tables (printed to stdout)."""
import json, re, sys, os, collections

R = json.load(open(sys.argv[1]))["results"]
SCALES = ["x1", "x2", "x3", "x4", "x4s"]
NN = ["x1", "x2", "x3", "x4"]


def parse(n):
    m = re.match(r"^(ft|before|after|normal|default)-(.*?)-?(grayscale|none)-(\d+)$", n)
    be, fam, aa, sz = m.groups()
    fam = fam or "Roboto"
    fam = {"Roboto-Regular": "Roboto", "NotoSans-Regular-Latin": "NotoSans", "SourceSerif4-Regular": "SourceSerif4"}.get(fam, fam)
    return be, fam, "gray" if aa == "grayscale" else "mono", int(sz)


rows = []
ideal = {n: per for n, per in R.items() if n.startswith("ideal-")}
for n, per in R.items():
    if n.startswith("ideal-"):
        continue
    be, fam, aa, sz = parse(n)
    best = min(NN, key=lambda s: (per[s]["cer"], per[s]["wer"]))
    rows.append(dict(name=n, be=be, fam=fam, aa=aa, sz=sz, best=best, per=per))

ORDER = {"ft": 0, "after": 1, "normal": 1, "before": 2, "default": 3}
rows.sort(key=lambda r: (r["fam"], r["sz"], r["aa"], ORDER[r["be"]]))

LABEL = {"ft": "FreeType", "after": "Native normal (after mono)", "before": "Native normal (before mono)",
         "normal": "Native normal", "default": "Native default (unhinted)"}


def pct(x):
    return f"{x*100:5.1f}%"


def table(sel, title):
    print(f"\n### {title}\n")
    print("| Family | Size | AA | Backend | Best up | CER | WER | WRec | Conf | CER x1 | CER x2 | CER x3 | CER x4 | CER x4 smooth |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for r in sel:
        p = r["per"]; b = r["best"]
        print(f"| {r['fam']} | {r['sz']} | {r['aa']} | {LABEL[r['be']]} | {b} | {pct(p[b]['cer'])} | {pct(p[b]['wer'])} | {pct(wrecall(r['name'], b))} | {p[b]['conf']:.1f} | "
              + " | ".join(pct(p[s]["cer"]) for s in SCALES) + " |")


GTW = collections.Counter(json.load(open(sys.argv[1]))["gt"].split())
RAW = os.path.join(os.path.dirname(os.path.abspath(sys.argv[1])), "raw")


def wrecall(name, scale):
    """Bag-of-words recall: fraction of ground-truth tokens that appear verbatim in the OCR output
    (robust to Tesseract merging/splitting words and shifting lines, unlike sequence WER)."""
    try:
        hw = collections.Counter(open(os.path.join(RAW, f"{name}_{scale}.txt"), encoding="utf-8").read().split())
    except FileNotFoundError:
        return float("nan")
    return sum(min(c, hw[w]) for w, c in GTW.items()) / sum(GTW.values())


print("## OCR ceiling: 'ideal' large renderings (64-96 px, grayscale, x1)\n")
print("| Rendering | CER | WER | word recall | Conf |")
print("|---|---|---|---|---|")
for n, per in sorted(ideal.items()):
    print(f"| {n} | {pct(per['x1']['cer'])} | {pct(per['x1']['wer'])} | {pct(wrecall(n, 'x1'))} | {per['x1']['conf']:.1f} |")
print("\n## Full results\n")
print("CER/WER/Conf columns are at the best nearest-neighbour upscale (by CER). 'x4 smooth' = Lanczos x4, a control, not eligible as 'best'. "
      "WER is sequence word error rate (inflated by Tesseract merging/splitting words); 'WRec' is bag-of-words recall of ground-truth tokens.")
table([r for r in rows if r["name"].startswith(("ft-R", "before-R", "after-R"))], "mono/base: Roboto, FreeType vs Native normal before/after mono renderer")
table([r for r in rows if "arial" in r["name"]], "mono/base: Arial 12")
table([r for r in rows if "NotoSans" in r["name"]], "mono/base: NotoSans")
table([r for r in rows if "SourceSerif" in r["name"]], "mono/base: SourceSerif4")
table([r for r in rows if r["be"] in ("normal", "default") or (r["be"] == "ft" and r["fam"] == "Roboto" and "Regular" not in r["name"])],
      "final: Roboto, FreeType vs Native normal vs Native default (unhinted)")

# ---- ranking checks -------------------------------------------------------
print("\n## Ranking checks (CER at best NN upscale; lower is better)\n")
by = {(r["fam"], r["sz"], r["aa"], r["be"], r["name"].split("-")[1] == "Regular" or r["fam"] != "Roboto" or r["be"] in ("before", "after")): r for r in rows}


def get(fam, sz, aa, be, group):
    for r in rows:
        if (r["fam"], r["sz"], r["aa"], r["be"]) == (fam, sz, aa, be) and (("Regular" in r["name"] or fam != "Roboto") == (group == "base")):
            return r
    return None


def cer_of(r, scale=None):
    return r["per"][scale or r["best"]]["cer"]


print("### final set: FreeType vs Native normal vs Native default\n")
print("| Size | AA | FT | normal | default | OCR order | expected FT <= normal <= default? |")
print("|---|---|---|---|---|---|---|")
for sz in (12, 16, 24):
    for aa in ("gray", "mono"):
        f, nrm, d = get("Roboto", sz, aa, "ft", "final"), get("Roboto", sz, aa, "normal", "final"), get("Roboto", sz, aa, "default", "final")
        if not (f and nrm and d):
            continue
        trio = sorted([("FT", cer_of(f)), ("normal", cer_of(nrm)), ("default", cer_of(d))], key=lambda t: t[1])
        ok = cer_of(f) <= cer_of(nrm) <= cer_of(d)
        print(f"| {sz} | {aa} | {pct(cer_of(f))} | {pct(cer_of(nrm))} | {pct(cer_of(d))} | {' < '.join(t[0] for t in trio)} | {'yes' if ok else 'NO'} |")

print("\n### base set: FreeType vs Native normal after vs before (mono renderer change)\n")
print("| Family | Size | AA | FT | after | before | OCR order | FT best? | after <= before? | after-before delta (pp) | mean |after-before| across NN upscales (pp) |")
print("|---|---|---|---|---|---|---|---|---|---|---|")
for fam in ("Roboto", "arial", "NotoSans", "SourceSerif4"):
    for sz in (8, 10, 12):
        for aa in ("gray", "mono"):
            f, a, b = get(fam, sz, aa, "ft", "base"), get(fam, sz, aa, "after", "base"), get(fam, sz, aa, "before", "base")
            if not (f and a and b):
                continue
            trio = sorted([("FT", cer_of(f)), ("after", cer_of(a)), ("before", cer_of(b))], key=lambda t: t[1])
            delta = (cer_of(a) - cer_of(b)) * 100
            spread = sum(abs(a["per"][s]["cer"] - b["per"][s]["cer"]) for s in NN) / len(NN) * 100
            print(f"| {fam} | {sz} | {aa} | {pct(cer_of(f))} | {pct(cer_of(a))} | {pct(cer_of(b))} | {' < '.join(t[0] for t in trio)} | "
                  f"{'yes' if trio[0][0]=='FT' else 'NO'} | {'yes' if cer_of(a) <= cer_of(b) else 'NO'} | {delta:+.1f} | {spread:.1f} |")

# ---- upscale behaviour ----------------------------------------------------
print("\n## Which upscale wins?\n")
cnt = collections.Counter(r["best"] for r in rows)
print("Best NN upscale counts over all renderings: " + ", ".join(f"{k}: {cnt[k]}" for k in NN))
mean_by_scale = {s: sum(r["per"][s]["cer"] for r in rows) / len(rows) for s in SCALES}
print("Mean CER by upscale over all renderings: " + ", ".join(f"{s}: {v*100:.1f}%" for s, v in mean_by_scale.items()))
small = [r for r in rows if r["sz"] <= 12]
mean_small = {s: sum(r["per"][s]["cer"] for r in small) / len(small) for s in SCALES}
print("Mean CER by upscale, sizes <= 12 only: " + ", ".join(f"{s}: {v*100:.1f}%" for s, v in mean_small.items()))
wins_smooth = sum(1 for r in rows if r["per"]["x4s"]["cer"] < r["per"][r["best"]]["cer"])
print(f"Lanczos x4 beats the best nearest-neighbour upscale in {wins_smooth}/{len(rows)} renderings.")

# ---- confusions -----------------------------------------------------------
print("\n## Top confusions (at best NN upscale, pooled over sizes 8-12 of the base set + final 12)\n")


def conf_table(sel, title, k=12):
    c = collections.Counter()
    dels = collections.Counter(); ins = collections.Counter()
    for r in sel:
        p = r["per"][r["best"]]
        for _, a, b in p["subs"]:
            c[(a, b)] += 1
        dels.update(p["dels"]); ins.update(p["ins"])
    print(f"**{title}** ({len(sel)} renderings)  ")
    print("subs: " + ", ".join(f"`{a}`->`{b}`x{n}" for (a, b), n in c.most_common(k)) + "  ")
    print("dropped: " + ", ".join(f"`{a}`x{n}" for a, n in dels.most_common(8)) + "  ")
    print("spurious: " + ", ".join(f"`{a}`x{n}" for a, n in ins.most_common(6)) + "\n")


for be in ("ft", "after", "before", "normal", "default"):
    for aa in ("gray", "mono"):
        sel = [r for r in rows if r["be"] == be and r["aa"] == aa and r["sz"] <= 12]
        if sel:
            conf_table(sel, f"{LABEL[be]} / {aa}")

# ---- word-level failures for the headline cells ---------------------------
print("\n## Word errors for Roboto 10/12 (best NN upscale)\n")
for r in rows:
    if r["fam"] == "Roboto" and r["sz"] in (10, 12) and r["be"] in ("ft", "after", "before", "normal", "default"):
        p = r["per"][r["best"]]
        we = [f"{a}->{b}" if a and b else (f"-{a}" if a else f"+{b}") for a, b in p["word_errs"]]
        print(f"- **{r['name']}** @{r['best']} ({len(we)} word errs): " + ", ".join(we[:40]))
