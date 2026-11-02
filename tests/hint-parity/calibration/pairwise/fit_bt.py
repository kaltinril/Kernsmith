"""Direction E: fit Bradley-Terry strengths and feature weights from pairwise judgments.

Run:  python -I fit_bt.py [results.json] [--pairs pairs.json] [--l2 1.0] [--boot 200]

Outputs: a ranking per variant and per variant x aa (with bootstrap SE), the feature table per
strip (features.json) and the proposed legibility weights (weights.json).
"""
import sys, os, json, argparse, math
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ap = argparse.ArgumentParser()
ap.add_argument("results", nargs="?", default=os.path.join(HERE, "results.json"))
ap.add_argument("--pairs", default=os.path.join(HERE, "pairs.json"))
ap.add_argument("--l2", type=float, default=1.0, help="L2 penalty on the standardised logistic weights")
ap.add_argument("--boot", type=int, default=200)
ap.add_argument("--seed", type=int, default=1)
args = ap.parse_args()

manifest = json.load(open(args.pairs))
strips = manifest["strips"]
results = json.load(open(args.results))
pair_by_id = {p["pair_id"]: p for p in manifest["pairs"]}
rng = np.random.default_rng(args.seed)

# ------------------------------------------------------------------ Bradley-Terry (MM, Hunter 2004)
def bt_fit(items, outcomes, iters=200):
    """outcomes: list of (winner_idx, loser_idx, weight). Ties are passed as two half-weight outcomes."""
    n = len(items)
    W = np.zeros(n)
    N = np.zeros((n, n))
    for w, l, wt in outcomes:
        W[w] += wt
        N[w, l] += wt
        N[l, w] += wt
    p = np.ones(n)
    for _ in range(iters):
        denom = np.zeros(n)
        for i in range(n):
            for j in range(n):
                if N[i, j] > 0:
                    denom[i] += N[i, j] / (p[i] + p[j])
        newp = np.where(denom > 0, (W + 0.01) / (denom + 1e-9), p)  # +0.01 prior keeps unbeaten items finite
        newp /= np.exp(np.mean(np.log(newp)))
        if np.max(np.abs(np.log(newp) - np.log(p))) < 1e-8:
            p = newp
            break
        p = newp
    return np.log(p)


def outcomes_for(keyfn):
    items = sorted({keyfn(strips[k]) for k in strips})
    idx = {it: i for i, it in enumerate(items)}
    outs = []
    for r in results:
        a, b = keyfn(strips[r["a"]]), keyfn(strips[r["b"]])
        if a == b:
            continue
        if r["winner"] == "a":
            outs.append((idx[a], idx[b], 1.0))
        elif r["winner"] == "b":
            outs.append((idx[b], idx[a], 1.0))
        else:
            outs.append((idx[a], idx[b], 0.5))
            outs.append((idx[b], idx[a], 0.5))
    return items, outs


def report_bt(title, keyfn):
    items, outs = outcomes_for(keyfn)
    if not outs:
        print(f"\n{title}: no comparable judgments")
        return
    est = bt_fit(items, outs)
    boots = []
    outs_arr = np.array(outs, dtype=object)
    for _ in range(args.boot):
        sample = [outs[i] for i in rng.integers(0, len(outs), len(outs))]
        boots.append(bt_fit(items, sample))
    se = np.std(np.array(boots), axis=0)
    print(f"\n{title}  ({len(results)} judgments, {sum(1 for r in results if r['winner']=='tie')} ties)")
    print(f"  {'item':24s} {'log-strength':>12s} {'+-SE':>6s}   P(beats mean)")
    for i in np.argsort(-est):
        print(f"  {str(items[i]):24s} {est[i]:12.2f} {se[i]:6.2f}   {1/(1+math.exp(-est[i])):.2f}")
    return dict(zip(map(str, items), zip(est.tolist(), se.tolist())))


# ------------------------------------------------------------------ features
def holes(mask):
    """Number of background regions not connected to the border (4-connected), mask: bool ink."""
    h, w = mask.shape
    bg = ~mask
    seen = np.zeros_like(bg)
    def fill(sy, sx):
        stack = [(sy, sx)]
        seen[sy, sx] = True
        while stack:
            y, x = stack.pop()
            for ny, nx in ((y-1, x), (y+1, x), (y, x-1), (y, x+1)):
                if 0 <= ny < h and 0 <= nx < w and bg[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
    for x in range(w):
        for y in (0, h-1):
            if bg[y, x] and not seen[y, x]:
                fill(y, x)
    for y in range(h):
        for x in (0, w-1):
            if bg[y, x] and not seen[y, x]:
                fill(y, x)
    count = 0
    for y in range(h):
        for x in range(w):
            if bg[y, x] and not seen[y, x]:
                count += 1
                fill(y, x)
    return count


def components(mask):
    """Number of 8-connected ink components."""
    h, w = mask.shape
    seen = np.zeros_like(mask)
    count = 0
    for y0 in range(h):
        for x0 in range(w):
            if mask[y0, x0] and not seen[y0, x0]:
                count += 1
                stack = [(y0, x0)]
                seen[y0, x0] = True
                while stack:
                    y, x = stack.pop()
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            ny, nx = y+dy, x+dx
                            if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                                seen[ny, nx] = True
                                stack.append((ny, nx))
    return count


def expected_components(text):
    # Two-piece glyphs in the sample text: i, j, %, ;, :, =, !, ?, ", ' etc.
    extra = {"i": 1, "j": 1, "%": 2, ":": 1, ";": 1, "=": 1, "!": 1, "?": 1, '"': 1}
    return sum(1 + extra.get(c, 0) for c in text if c != " ")


def features(info):
    img = np.asarray(Image.open(os.path.join(HERE, info["png"])).convert("L")).astype(np.float32)
    lh = info["line_height"]
    pad = 3
    lit = img >= 32
    strong = img >= 192
    f = {}
    f["ink_concentration"] = float(strong.sum() / max(1, lit.sum()))
    f["mean_ink"] = float(img[lit].mean() / 255 if lit.any() else 0)
    gy, gx = np.gradient(img)
    g = np.sqrt(gx*gx + gy*gy)
    f["sharpness"] = float(g[lit].mean() if lit.any() else 0)
    # Per line: holes at 64 vs 128 (broken rings), components vs expected, stems at mid x-height, gaps.
    broken, holes128, topo_err, stem_widths, gaps = 0, 0, 0, [], []
    for li, text in enumerate(manifest["text"]):
        y0, y1 = pad + li*lh, pad + (li+1)*lh
        band = img[y0:y1]
        m128 = band >= 128
        m64 = band >= 64
        h64, h128 = holes(m64), holes(m128)
        broken += max(0, h64 - h128)
        holes128 += h128
        topo_err += abs(components(m128) - expected_components(text))
        # Stem widths: the row in the lower 65% of the line with the most ink runs (mid x-height).
        best_row, best_runs = None, -1
        for r in range(int(lh*0.35), lh):
            row = m128[r]
            runs = int(np.sum(row[1:] & ~row[:-1]) + (1 if row[0] else 0))
            if runs > best_runs:
                best_runs, best_row = runs, r
        if best_row is not None:
            row = m128[best_row]
            widths, cur = [], 0
            for v in row:
                if v:
                    cur += 1
                elif cur:
                    widths.append(cur); cur = 0
            if cur:
                widths.append(cur)
            stem_widths += widths
        # Inter-glyph ink gaps within words (ink columns from the glyph boxes).
        prev_right = None
        for ch, bx0, bx1 in info["boxes"][li]:
            cols = np.where((band[:, bx0:bx1] >= 128).any(axis=0))[0]
            if len(cols) == 0:
                prev_right = None
                continue
            left, right = bx0 + cols[0], bx0 + cols[-1] + 1
            if prev_right is not None:
                gap = left - prev_right
                if -2 <= gap <= info["size"] * 0.6:  # exclude word spaces
                    gaps.append(gap)
            prev_right = right
    f["broken_rings"] = float(broken)
    f["holes_128"] = float(holes128)
    f["topology_error"] = float(topo_err)
    f["stem_width_var"] = float(np.var(stem_widths)) if stem_widths else 0.0
    f["gap_var"] = float(np.var(gaps)) if gaps else 0.0
    f["gap_mean"] = float(np.mean(gaps)) if gaps else 0.0
    f["touching_pairs"] = float(sum(1 for g in gaps if g <= 0))
    return f


print("computing features for", len(strips), "strips ...")
feats = {k: features(v) for k, v in strips.items()}
json.dump(feats, open(os.path.join(HERE, "features.json"), "w"), indent=1)
FEATURE_NAMES = list(next(iter(feats.values())).keys())

# ------------------------------------------------------------------ BT rankings
bt_variant = report_bt("Bradley-Terry by variant", lambda s: s["variant"])
bt_variant_aa = report_bt("Bradley-Terry by variant x anti-alias", lambda s: f"{s['variant']}/{s['aa']}")
report_bt("Bradley-Terry by variant x font", lambda s: f"{s['variant']}/{s['font']}")

# ------------------------------------------------------------------ logistic regression on feature differences
X, y = [], []
for r in results:
    if r["winner"] not in ("a", "b"):
        continue
    fa, fb = feats[r["a"]], feats[r["b"]]
    d = np.array([fa[n] - fb[n] for n in FEATURE_NAMES])
    X.append(d); y.append(1.0 if r["winner"] == "a" else 0.0)
    X.append(-d); y.append(0.0 if r["winner"] == "a" else 1.0)  # symmetrise (no intercept)
X = np.array(X); y = np.array(y)
allf = np.array([[feats[k][n] for n in FEATURE_NAMES] for k in strips])
scale = allf.std(axis=0)
scale[scale == 0] = 1.0
Xs = X / scale

w = np.zeros(len(FEATURE_NAMES))
if len(y):
    lam = args.l2
    for _ in range(100):  # Newton with L2 (no intercept: pairs are symmetric)
        z = Xs @ w
        p = 1 / (1 + np.exp(-z))
        grad = Xs.T @ (p - y) + lam * w
        H = (Xs * (p * (1 - p))[:, None]).T @ Xs + lam * np.eye(len(w))
        step = np.linalg.solve(H, grad)
        w -= step
        if np.max(np.abs(step)) < 1e-9:
            break
    z = Xs @ w
    p = 1 / (1 + np.exp(-z))
    acc = float(np.mean((p > 0.5) == (y > 0.5)))
    # Leave-one-pair-out accuracy
    hits = 0
    npairs = len(y) // 2
    for k in range(npairs):
        keep = np.ones(len(y), bool); keep[2*k:2*k+2] = False
        wk = np.zeros(len(w))
        for _ in range(50):
            zk = Xs[keep] @ wk; pk = 1/(1+np.exp(-zk))
            g = Xs[keep].T @ (pk - y[keep]) + lam*wk
            Hk = (Xs[keep]*(pk*(1-pk))[:,None]).T @ Xs[keep] + lam*np.eye(len(w))
            s = np.linalg.solve(Hk, g); wk -= s
            if np.max(np.abs(s)) < 1e-9: break
        hits += int(((Xs[2*k] @ wk) > 0) == (y[2*k] > 0.5))
    loo = hits / max(1, npairs)
else:
    acc = loo = float("nan")

print(f"\nLogistic regression on feature differences (L2={args.l2}, {len(y)//2} decisive pairs)")
print(f"  train accuracy {acc:.2f}, leave-one-pair-out accuracy {loo:.2f}  (0.5 = chance)")
print(f"  {'feature':20s} {'weight/sd':>10s}   direction")
for n, wi in sorted(zip(FEATURE_NAMES, w), key=lambda t: -abs(t[1])):
    print(f"  {n:20s} {wi:10.3f}   {'preferred higher' if wi > 0 else 'preferred lower'}")

weights = {
    "note": "Weights of a linear legibility score: score(strip) = sum_f weight[f] * feature[f] / scale[f]. "
            "Fitted as a symmetric logistic model of P(a preferred over b) on feature differences; the sign says "
            "which direction the owner preferred, the magnitude is per standard deviation of the feature across the "
            "63 strips. Mapping to tests/hint-parity --legibility: ink_concentration -> 'ink concentration'; "
            "sharpness -> 'alignment'/edge hardness; stem_width_var -> 'stem evenness' (negated); broken_rings + "
            "holes_128 + topology_error -> 'topology' (negated error terms); gap_var + touching_pairs -> 'spacing' "
            "(negated); mean_ink -> fidelity/contrast. Features here are strip-level proxies, not the scorer's per-glyph "
            "measures: port the SIGNS and the relative magnitudes, re-fit the absolute scale on the scorer's own features.",
    "n_judgments": len(results),
    "n_decisive_pairs": int(len(y) // 2),
    "l2": args.l2,
    "train_accuracy": acc,
    "leave_one_out_accuracy": loo,
    "weights_per_sd": {n: float(wi) for n, wi in zip(FEATURE_NAMES, w)},
    "feature_sd": {n: float(s) for n, s in zip(FEATURE_NAMES, scale)},
    "weights_raw": {n: float(wi / s) for n, wi, s in zip(FEATURE_NAMES, w, scale)},
    "bradley_terry": {"by_variant": bt_variant, "by_variant_aa": bt_variant_aa},
}
json.dump(weights, open(os.path.join(HERE, "weights.json"), "w"), indent=1)
print("\nwrote", os.path.join(HERE, "weights.json"), "and features.json")
