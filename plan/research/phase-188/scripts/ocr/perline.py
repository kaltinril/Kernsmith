"""Per-line OCR (--psm 7, single text line) so that page-segmentation / line-merge artefacts
cannot pollute the score. Reports CER per line class:
  words  = lines 0,1,6,7,8,9,10 (natural words / pangrams)
  alpha  = lines 2,3 (A-Z, a-z)
  digits = line 4
  confus = line 5 (rn m cl d Illegal 1Il O0 8B 6b 5S 2Z 7T vv w)
Usage: python -I perline.py <outdir> <dir1> <names1> [<dir2> <names2> ...]"""
import sys, os, json, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ocr_bench as ob
from PIL import Image

CLASS = {0: "words", 1: "words", 2: "alpha", 3: "alpha", 4: "digits", 5: "confus", 6: "words", 7: "words", 8: "words", 9: "words", 10: "words"}
out = sys.argv[1]
os.makedirs(os.path.join(out, "img"), exist_ok=True)
os.makedirs(os.path.join(out, "raw"), exist_ok=True)
args = sys.argv[2:]
jobs = [(d, n) for d, names in zip(args[::2], args[1::2]) for n in names.split(",")]


def tess_line(ip, base):
    subprocess.run([ob.TESS, ip, base, "--psm", "7", "-l", "eng", "txt"], check=True, capture_output=True)
    return open(base + ".txt", encoding="utf-8").read()


res = {}
for d, n in jobs:
    res[n] = {}
    for k in (2, 3, 4):
        per = {}
        tot = {c: [0, 0] for c in set(CLASS.values())}
        tot["all"] = [0, 0]
        for li, line in enumerate(ob.CORPUS):
            img = ob.render(d, n, [line], margin=10, leading=1.6)
            img = img.resize((img.width * k, img.height * k), Image.NEAREST)
            ip = os.path.join(out, "img", f"{n}_x{k}_L{li:02d}.png"); img.save(ip)
            txt = tess_line(ip, os.path.join(out, "raw", f"{n}_x{k}_L{li:02d}"))
            gt = line.replace(" ", ""); hy = ob.norm(txt).replace("\n", "").replace(" ", "")
            dist, ops = ob.lev_align(gt, hy)
            per[li] = dict(dist=dist, n=len(gt), hyp=ob.norm(txt), subs=[(a, b) for o, a, b in ops if o == "sub"])
            tot[CLASS[li]][0] += dist; tot[CLASS[li]][1] += len(gt)
            tot["all"][0] += dist; tot["all"][1] += len(gt)
        cer = {c: v[0] / v[1] * 100 for c, v in tot.items()}
        res[n][f"x{k}"] = dict(cer=cer, lines=per)
        print(f"{n:40s} x{k} CER% all={cer['all']:5.1f} words={cer['words']:5.1f} alpha={cer['alpha']:5.1f} digits={cer['digits']:5.1f} confus={cer['confus']:5.1f}", flush=True)
json.dump(res, open(os.path.join(out, "perline.json"), "w"), indent=1)
