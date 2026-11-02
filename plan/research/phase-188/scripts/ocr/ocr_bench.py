"""OCR legibility bench: render a fixed corpus with each BMFont (.fnt + _0.png),
upscale (nearest x1..x4, plus a Lanczos x4 control), OCR with Tesseract and
score CER / WER / confidence against the ground truth.

Usage: python -I ocr_bench.py <outdir> <dir1> <names1> [<dir2> <names2> ...]
  names = comma-separated .fnt basenames (use '*' for every .fnt in dir)
"""
import sys, re, os, json, subprocess, glob, collections
from PIL import Image

CORPUS = [
    "The quick brown fox jumps over the lazy dog.",
    "Pack my box with five dozen liquor jugs!",
    "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
    "abcdefghijklmnopqrstuvwxyz",
    "0123456789 1234 5678 9012 3456 7890",
    "rn m cl d Illegal 1Il O0 8B 6b 5S 2Z 7T vv w",
    "Hamburgefonstiv WMgy illicit minimum",
    "the and that have with this from they will",
    "would there their what about which when make",
    "time year people water first great little",
    "Error: file not found at line 42, column 7.",
]
GT = "\n".join(CORPUS)
TESS = r"C:\Program Files\Tesseract-OCR\tesseract.exe"


def load(d, n):
    chars, kern, lh = {}, {}, 0
    for line in open(os.path.join(d, n + ".fnt"), encoding="utf-8", errors="replace"):
        t = line.split()[0] if line.strip() else ""
        kv = dict(re.findall(r'(\w+)=("[^"]*"|\S+)', line))
        if t == "common":
            lh = int(kv["lineHeight"])
        if t == "char":
            chars[int(kv["id"])] = {k: int(v) for k, v in kv.items() if k != "chnl" and not v.startswith('"')}
        if t == "kerning":
            kern[(int(kv["first"]), int(kv["second"]))] = int(kv["amount"])
    return lh, chars, kern, Image.open(os.path.join(d, n + "_0.png")).convert("RGBA")


def render(d, n, lines, margin=12, leading=1.6):
    """Render dark text on a white background. Returns 'L' image at 1x."""
    lh, chars, kern, atlas = load(d, n)
    step = max(lh + 2, int(round(lh * leading)))
    widths = []
    for s in lines:
        x, prev = 0, None
        for ch in s:
            c = chars.get(ord(ch))
            if not c:
                continue
            if prev is not None:
                x += kern.get((prev, ord(ch)), 0)
            x += c["xadvance"]
            prev = ord(ch)
        widths.append(x)
    W = max(widths) + 2 * margin + 4
    H = step * len(lines) + 2 * margin
    # Coverage image (white ink on black), then invert at the end.
    img = Image.new("L", (W, H), 0)
    for li, s in enumerate(lines):
        x, y, prev = margin, margin + li * step, None
        for ch in s:
            c = chars.get(ord(ch))
            if not c:
                continue
            if prev is not None:
                x += kern.get((prev, ord(ch)), 0)
            if c["width"] and c["height"]:
                g = atlas.crop((c["x"], c["y"], c["x"] + c["width"], c["y"] + c["height"]))
                a = g.split()[3] if g.getextrema()[3][0] < 255 else g.convert("L")
                px, py = x + c["xoffset"], y + c["yoffset"]
                cur = img.crop((px, py, px + c["width"], py + c["height"]))
                img.paste(Image.composite(Image.new("L", cur.size, 255), cur, a), (px, py))
            x += c["xadvance"]
            prev = ord(ch)
    return img.point(lambda v: 255 - v)  # dark on light


def tesseract(img_path, base):
    """Run tesseract; return (text, mean word confidence, per-word list)."""
    cmd = [TESS, img_path, base, "--psm", "6", "-l", "eng", "txt", "tsv"]
    subprocess.run(cmd, check=True, capture_output=True)
    text = open(base + ".txt", encoding="utf-8").read()
    confs, words = [], []
    for row in open(base + ".tsv", encoding="utf-8").read().splitlines()[1:]:
        f = row.split("\t")
        if len(f) >= 12 and f[0] == "5" and f[11].strip():
            confs.append(float(f[10]))
            words.append((f[11], float(f[10])))
    return text, (sum(confs) / len(confs) if confs else 0.0), words


def lev_align(a, b):
    """Levenshtein distance between sequences a (ref) and b (hyp) with backtrace.
    Returns (distance, ops) where ops are ('sub',ra,hb) ('del',ra,None) ('ins',None,hb)."""
    n, m = len(a), len(b)
    D = [[0] * (m + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        D[i][0] = i
    for j in range(1, m + 1):
        D[0][j] = j
    for i in range(1, n + 1):
        ai = a[i - 1]
        for j in range(1, m + 1):
            c = 0 if ai == b[j - 1] else 1
            D[i][j] = min(D[i - 1][j] + 1, D[i][j - 1] + 1, D[i - 1][j - 1] + c)
    ops, i, j = [], n, m
    while i > 0 or j > 0:
        if i > 0 and j > 0 and D[i][j] == D[i - 1][j - 1] + (0 if a[i - 1] == b[j - 1] else 1):
            if a[i - 1] != b[j - 1]:
                ops.append(("sub", a[i - 1], b[j - 1]))
            i, j = i - 1, j - 1
        elif i > 0 and D[i][j] == D[i - 1][j] + 1:
            ops.append(("del", a[i - 1], None)); i -= 1
        else:
            ops.append(("ins", None, b[j - 1])); j -= 1
    return D[n][m], ops[::-1]


def norm(t):
    return re.sub(r"[ \t]+", " ", "\n".join(l.strip() for l in t.strip().splitlines() if l.strip()))


def score(gt, hyp):
    g, h = norm(gt), norm(hyp)
    # character level, ignoring whitespace entirely (spacing is a layout choice, not legibility)
    gc, hc = g.replace("\n", " ").replace(" ", ""), h.replace("\n", " ").replace(" ", "")
    dist, ops = lev_align(gc, hc)
    cer = dist / len(gc)
    gw, hw = g.split(), h.split()
    wdist, wops = lev_align(gw, hw)
    wer = wdist / len(gw)
    # Also line-aware char diff (with spaces) for a readable per-line view
    return dict(cer=cer, wer=wer, char_ops=ops, word_ops=wops, nchar=len(gc), nword=len(gw))


def main():
    out = sys.argv[1]
    os.makedirs(os.path.join(out, "img"), exist_ok=True)
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    jobs = []
    args = sys.argv[2:]
    for d, names in zip(args[::2], args[1::2]):
        if names == "*":
            ns = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(d, "*.fnt")))
        else:
            ns = names.split(",")
        jobs += [(d, n) for n in ns]
    scales = [("x1", 1, Image.NEAREST), ("x2", 2, Image.NEAREST), ("x3", 3, Image.NEAREST),
              ("x4", 4, Image.NEAREST), ("x4s", 4, Image.LANCZOS)]
    results = {}
    for d, n in jobs:
        base1 = render(d, n, CORPUS)
        results[n] = {}
        for tag, k, flt in scales:
            img = base1 if k == 1 else base1.resize((base1.width * k, base1.height * k), flt)
            ip = os.path.join(out, "img", f"{n}_{tag}.png")
            img.save(ip)
            rb = os.path.join(out, "raw", f"{n}_{tag}")
            text, conf, words = tesseract(ip, rb)
            sc = score(GT, text)
            results[n][tag] = dict(cer=sc["cer"], wer=sc["wer"], conf=conf, nchar=sc["nchar"], nword=sc["nword"],
                                   subs=[o for o in sc["char_ops"] if o[0] == "sub"],
                                   dels=[o[1] for o in sc["char_ops"] if o[0] == "del"],
                                   ins=[o[2] for o in sc["char_ops"] if o[0] == "ins"],
                                   word_errs=[(o[1], o[2]) for o in sc["word_ops"]])
            print(f"{n:40s} {tag:4s} CER={sc['cer']*100:6.2f}%  WER={sc['wer']*100:6.2f}%  conf={conf:5.1f}", flush=True)
    json.dump(dict(gt=GT, results=results), open(os.path.join(out, "results.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
