"""Noise floor: re-render the same font with slightly different page layout (margin / leading)
and re-OCR. The spread of CER across layouts, with NO renderer change, bounds the smallest
renderer difference OCR could reliably detect.
Usage: python -I noise.py <outdir> <dir> <names-csv>"""
import sys, os, json, statistics
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ocr_bench as ob
from PIL import Image

out, d, names = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
os.makedirs(out, exist_ok=True)
LAYOUTS = [(12, 1.6), (13, 1.6), (14, 1.9), (11, 1.9), (12, 2.2)]
res = {}
for n in names:
    res[n] = {}
    for sc in (2, 3):
        cers, wrs = [], []
        for mg, ld in LAYOUTS:
            img = ob.render(d, n, ob.CORPUS, margin=mg, leading=ld)
            img = img.resize((img.width * sc, img.height * sc), Image.NEAREST)
            ip = os.path.join(out, f"{n}_x{sc}_m{mg}_l{ld}.png"); img.save(ip)
            text, conf, _ = ob.tesseract(ip, ip[:-4])
            s = ob.score(ob.GT, text)
            cers.append(s["cer"] * 100)
        res[n][f"x{sc}"] = dict(cers=cers, mean=statistics.mean(cers), sd=statistics.pstdev(cers), rng=max(cers) - min(cers))
        print(f"{n:40s} x{sc} CER% per layout: " + " ".join(f"{c:5.1f}" for c in cers) +
              f"  mean={statistics.mean(cers):5.1f} sd={statistics.pstdev(cers):4.1f} range={max(cers)-min(cers):4.1f}", flush=True)
json.dump(res, open(os.path.join(out, "noise.json"), "w"), indent=1)
