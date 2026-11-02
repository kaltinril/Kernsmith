"""Round 2 (current Native build): build pairwise.html (self-contained, file://) from the render matrix.

Design is weighted toward nn-vs-ft and nn-vs-nd in both anti-alias modes, with a few gdi anchors.

Run:  python -I make_pairs.py [--renders DIR] [--out DIR]

Writes pairwise.html, pairs.json (manifest incl. per-strip metrics) and strips/*.png.
"""
import sys, os, re, io, json, base64, random, argparse
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser()
ap.add_argument("--renders", default=os.path.join(HERE, "renders"))
ap.add_argument("--out", default=HERE)
ap.add_argument("--seed", type=int, default=20261102)
args = ap.parse_args()
RENDERS = os.path.abspath(args.renders)
OUT = os.path.abspath(args.out)
STRIPS = os.path.join(OUT, "strips")
os.makedirs(STRIPS, exist_ok=True)

FONTS = ["roboto", "verdana", "georgia"]
SIZES = [10, 11, 12]
AAS = ["grayscale", "none"]
VARIANTS = {"grayscale": ["ft", "nn", "nd"], "none": ["ft", "nn", "nd", "gdi"]}
LABELS = {"ft": "FreeType hinted", "nn": "Native normal (current, default)", "nd": "Native --hint-level none", "gdi": "GDI"}
TEXT = ["The quick brown fox jumps over 13 lazy dogs.", "Hamburgefonstiv 0123456789 %@&"]
PAD = 3


def load(base):
    chars, kern, lh = {}, {}, 0
    for line in open(base + ".fnt", encoding="utf-8", errors="replace"):
        t = line.split()[0] if line.strip() else ""
        kv = dict(re.findall(r'(\w+)=("[^"]*"|\S+)', line))
        if t == "common":
            lh = int(kv["lineHeight"])
        if t == "char":
            chars[int(kv["id"])] = {k: int(v) for k, v in kv.items() if k != "chnl" and not v.startswith('"')}
        if t == "kerning":
            kern[(int(kv["first"]), int(kv["second"]))] = int(kv["amount"])
    return lh, chars, kern, Image.open(base + "_0.png").convert("RGBA")


def render(base):
    """White-on-black 'L' strip at 1:1 plus per-line glyph ink boxes (x0, x1) in strip coords."""
    lh, chars, kern, atlas = load(base)
    width = 0
    for s in TEXT:
        x, prev = 0, None
        for ch in s:
            c = chars.get(ord(ch))
            if not c:
                continue
            if prev:
                x += kern.get((prev, ord(ch)), 0)
            x += c["xadvance"]
            prev = ord(ch)
        width = max(width, x)
    img = Image.new("L", (width + 2 * PAD, lh * len(TEXT) + 2 * PAD), 0)
    boxes = []
    for li, s in enumerate(TEXT):
        x, y, prev = PAD, PAD + li * lh, None
        line_boxes = []
        for ch in s:
            c = chars.get(ord(ch))
            if not c:
                continue
            if prev:
                x += kern.get((prev, ord(ch)), 0)
            if c["width"] and c["height"]:
                g = atlas.crop((c["x"], c["y"], c["x"] + c["width"], c["y"] + c["height"]))
                a = g.split()[3] if g.getextrema()[3][0] < 255 else g.convert("L")
                px, py = x + c["xoffset"], y + c["yoffset"]
                cur = img.crop((px, py, px + c["width"], py + c["height"]))
                img.paste(Image.composite(Image.new("L", cur.size, 255), cur, a), (px, py))
                if ch != " ":
                    line_boxes.append([ch, px, px + c["width"]])
            x += c["xadvance"]
            prev = ord(ch)
        boxes.append(line_boxes)
    return img, boxes, lh


def data_uri(img):
    b = io.BytesIO()
    img.save(b, format="PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(b.getvalue()).decode()


# ---- render every condition strip ----
strips = {}  # key -> dict
for f in FONTS:
    for s in SIZES:
        for aa in AAS:
            for v in VARIANTS[aa]:
                base = os.path.join(RENDERS, f"{f}_{s}_{v}_{aa}")
                if not os.path.exists(base + ".fnt"):
                    print("missing", base, file=sys.stderr)
                    continue
                img, boxes, lh = render(base)
                key = f"{f}_{s}_{aa}_{v}"
                path = os.path.join(STRIPS, key + ".png")
                img.save(path)
                strips[key] = dict(key=key, font=f, size=s, aa=aa, variant=v, label=LABELS[v],
                                   png=os.path.relpath(path, OUT).replace("\\", "/"), fnt=base + ".fnt",
                                   width=img.width, height=img.height, line_height=lh, boxes=boxes,
                                   uri=data_uri(img))

# ---- weighted pair design ----
# Per anti-alias mode: (variant a, variant b, number of pairs). 9 = one per font x size condition;
# smaller counts are spread over distinct fonts (and distinct sizes where possible).
DESIGN = {
    "grayscale": [("nn", "ft", 9), ("nn", "nd", 9), ("ft", "nd", 3)],
    "none":      [("nn", "ft", 9), ("nn", "nd", 9), ("nn", "gdi", 3), ("ft", "gdi", 2)],
}
rng = random.Random(args.seed)
pairs = []
pid = 0
conds = [(f, s) for f in FONTS for s in SIZES]
for aa in AAS:
    for a, b, n in DESIGN[aa]:
        if n >= len(conds):
            chosen = list(conds)
        else:
            # one condition per font first (distinct sizes), then fill the rest at random
            fonts = list(FONTS); rng.shuffle(fonts)
            sizes = list(SIZES); rng.shuffle(sizes)
            chosen = [(fonts[i], sizes[i % len(sizes)]) for i in range(min(n, len(fonts)))]
            rest = [c for c in conds if c not in chosen]; rng.shuffle(rest)
            chosen += rest[:n - len(chosen)]
        for (f, sz) in chosen:
            ka, kb = f"{f}_{sz}_{aa}_{a}", f"{f}_{sz}_{aa}_{b}"
            if ka not in strips or kb not in strips:
                print("skip", ka, kb, file=sys.stderr)
                continue
            if rng.random() < 0.5:
                ka, kb = kb, ka
            pairs.append(dict(pair_id=pid, font=f, size=sz, aa=aa, a=ka, b=kb))
            pid += 1
rng.shuffle(pairs)
for i, p in enumerate(pairs):
    p["order"] = i

manifest = dict(seed=args.seed, text=TEXT, labels=LABELS,
                strips={k: {kk: vv for kk, vv in v.items() if kk != "uri"} for k, v in strips.items()},
                pairs=pairs)
json.dump(manifest, open(os.path.join(OUT, "pairs.json"), "w"), indent=1)

# ---- the page ----
page_pairs = [dict(pair_id=p["pair_id"], font=p["font"], size=p["size"], aa=p["aa"], a=p["a"], b=p["b"],
                   ua=strips[p["a"]]["uri"], ub=strips[p["b"]]["uri"],
                   wa=strips[p["a"]]["width"], ha=strips[p["a"]]["height"],
                   wb=strips[p["b"]]["width"], hb=strips[p["b"]]["height"]) for p in pairs]

html = r"""<!doctype html>
<html><head><meta charset="utf-8"><title>Which reads better? (round 2)</title>
<style>
:root{--bg:#1b1b1b;--fg:#eee;--panel:#262626;--accent:#f5b400}
body.light{--bg:#fafafa;--fg:#111;--panel:#eee;--accent:#b36b00}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,Segoe UI,sans-serif}
header{display:flex;gap:16px;align-items:center;padding:10px 16px;background:var(--panel);flex-wrap:wrap}
.textline{width:100%;padding:6px 16px 10px;background:var(--panel);font-size:13px;border-bottom:1px solid #444}
.textline code{background:var(--bg);padding:1px 6px;border-radius:4px}
header b{color:var(--accent)}
main{padding:16px;max-width:1400px;margin:0 auto}
.row{display:flex;gap:16px;flex-wrap:wrap}
.card{flex:1 1 560px;background:var(--panel);border:2px solid transparent;border-radius:8px;padding:12px;cursor:pointer}
.card:hover{border-color:var(--accent)}
.card h3{margin:0 0 8px;font-size:13px;opacity:.8}
.s1,.s2{display:block;image-rendering:pixelated;image-rendering:crisp-edges;margin:6px 0}
body.light .s1,body.light .s2{filter:invert(1)}
.ctrl{margin:14px 0;display:flex;gap:10px;align-items:center;flex-wrap:wrap}
button{background:var(--panel);color:var(--fg);border:1px solid #666;border-radius:6px;padding:6px 12px;cursor:pointer;font:inherit}
button:hover{border-color:var(--accent)}
progress{width:260px}
textarea{width:100%;height:220px;background:var(--panel);color:var(--fg);border:1px solid #666;font:12px monospace}
.hint{opacity:.7;font-size:12px}
#done{display:none}
</style></head><body>
<header><b>Which reads better?</b>
<span id="prog"></span><progress id="bar" value="0" max="1"></progress>
<button id="theme">light / dark</button>
<span class="hint">Press <kbd>1</kbd> for left, <kbd>2</kbd> for right, <kbd>0</kbd> can't tell. Don't look for the "right" answer: pick what you would rather read.</span></header>
<div class="textline"><b>Round 2</b> (current Native build). Every strip shows the same two lines, 1:1 above and 2x below: line 1 <code>__TEXT1__</code> &nbsp; line 2 <code>__TEXT2__</code></div>
<main>
<div id="trial">
  <div class="hint" id="cond"></div>
  <div class="row">
    <div class="card" id="cardA"><h3>1 — left</h3><img class="s1" id="imgA1"><img class="s2" id="imgA2"></div>
    <div class="card" id="cardB"><h3>2 — right</h3><img class="s1" id="imgB1"><img class="s2" id="imgB2"></div>
  </div>
  <div class="ctrl"><button id="tie">0 — can't tell</button><button id="undo">undo</button></div>
</div>
<div id="done">
  <h2>Done — thanks.</h2>
  <p>Save <code>results.json</code> next to <code>pairs.json</code> and run <code>python -I fit_bt.py</code>.</p>
  <div class="ctrl"><button id="dl">download results.json</button><button id="again">judge again (new order)</button></div>
  <textarea id="out"></textarea>
</div>
</main>
<script>
const PAIRS = __PAIRS__;
let order = PAIRS.map((_, i) => i);
let i = 0, results = [], t0 = 0;
const $ = id => document.getElementById(id);
function show() {
  if (i >= order.length) return finish();
  const p = PAIRS[order[i]];
  $("prog").textContent = `pair ${i + 1} of ${order.length}`;
  $("bar").value = i / order.length;
  $("cond").textContent = `${p.font} ${p.size}px, ${p.aa === "none" ? "no anti-aliasing" : "grayscale"} — same text, two renderings (1:1 above, 2x below)`;
  for (const [img, uri, w, h] of [["imgA1", p.ua, p.wa, p.ha], ["imgB1", p.ub, p.wb, p.hb]]) { const e = $(img); e.src = uri; e.width = w; e.height = h; }
  for (const [img, uri, w, h] of [["imgA2", p.ua, p.wa, p.ha], ["imgB2", p.ub, p.wb, p.hb]]) { const e = $(img); e.src = uri; e.width = w * 2; e.height = h * 2; }
  t0 = performance.now();
}
function choose(w) {
  const p = PAIRS[order[i]];
  results.push({ pair_id: p.pair_id, a: p.a, b: p.b, winner: w, ms: Math.round(performance.now() - t0), theme: document.body.classList.contains("light") ? "light" : "dark" });
  i++; show();
}
function finish() {
  $("trial").style.display = "none"; $("done").style.display = "block";
  $("prog").textContent = `done (${results.length})`; $("bar").value = 1;
  $("out").value = JSON.stringify(results, null, 1);
}
$("cardA").onclick = () => choose("a"); $("cardB").onclick = () => choose("b"); $("tie").onclick = () => choose("tie");
$("undo").onclick = () => { if (i > 0 && results.length) { results.pop(); i--; show(); } };
$("theme").onclick = () => document.body.classList.toggle("light");
document.addEventListener("keydown", e => {
  if (i >= order.length) return;
  if (e.key === "1" || e.key === "ArrowLeft") choose("a");
  else if (e.key === "2" || e.key === "ArrowRight") choose("b");
  else if (e.key === "0" || e.key === " ") { e.preventDefault(); choose("tie"); }
  else if (e.key === "Backspace") { e.preventDefault(); $("undo").onclick(); }
});
$("dl").onclick = () => {
  const blob = new Blob([JSON.stringify(results, null, 1)], { type: "application/json" });
  const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = "results.json"; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
};
$("again").onclick = () => { for (let k = order.length - 1; k > 0; k--) { const j = Math.floor(Math.random() * (k + 1)); [order[k], order[j]] = [order[j], order[k]]; } i = 0; results = []; $("trial").style.display = ""; $("done").style.display = "none"; show(); };
show();
</script></body></html>
"""
html = html.replace("__PAIRS__", json.dumps(page_pairs)).replace("__TEXT1__", TEXT[0].replace("&", "&amp;")).replace("__TEXT2__", TEXT[1].replace("&", "&amp;"))
out_html = os.path.join(OUT, "pairwise.html")
open(out_html, "w", encoding="utf-8").write(html)
print(f"{len(strips)} strips, {len(pairs)} pairs -> {out_html} ({os.path.getsize(out_html)/1024:.0f} KB)")
from collections import Counter
print("pair types:", Counter(tuple(sorted((p['a'].split('_')[-1], p['b'].split('_')[-1]))) + (p['aa'],) for p in pairs))
