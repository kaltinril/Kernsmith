"""Noise floor probe 2: after nearest-neighbour upscaling by k, shift the image by (dx,dy) in
0..k-1 output pixels (i.e. sub-glyph-pixel phase changes relative to Tesseract's internal grid).
No renderer change -> the CER spread is OCR noise.
Usage: python -I noise2.py <outdir> <dir> <names-csv>"""
import sys, os, json, statistics
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ocr_bench as ob
from PIL import Image

out, d, names = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
os.makedirs(out, exist_ok=True)
res = {}
for n in names:
    base = ob.render(d, n, ob.CORPUS)
    res[n] = {}
    for k in (2, 3):
        up = base.resize((base.width * k, base.height * k), Image.NEAREST)
        cers = []
        for dx in range(k):
            for dy in range(k):
                img = Image.new("L", (up.width + k, up.height + k), 255)
                img.paste(up, (dx, dy))
                ip = os.path.join(out, f"{n}_x{k}_s{dx}{dy}.png"); img.save(ip)
                text, conf, _ = ob.tesseract(ip, ip[:-4])
                cers.append(ob.score(ob.GT, text)["cer"] * 100)
        res[n][f"x{k}"] = dict(cers=cers, mean=statistics.mean(cers), sd=statistics.pstdev(cers), rng=max(cers) - min(cers))
        print(f"{n:40s} x{k} CER% per shift: " + " ".join(f"{c:5.1f}" for c in cers) +
              f"  mean={statistics.mean(cers):5.1f} sd={statistics.pstdev(cers):4.1f} range={max(cers)-min(cers):4.1f}", flush=True)
json.dump(res, open(os.path.join(out, "noise2.json"), "w"), indent=1)
