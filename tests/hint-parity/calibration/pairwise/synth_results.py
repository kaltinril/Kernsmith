"""Synthetic judgments with a known order (ft > nn > nd > gdi), to test fit_bt.py recovers it.

Run:  python -I synth_results.py [seed] [out.json]
"""
import json, random, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
rank = {"ft": 3, "nn": 2, "nd": 1, "gdi": 0}
rng = random.Random(int(sys.argv[1]) if len(sys.argv) > 1 else 7)
m = json.load(open(os.path.join(HERE, "pairs.json")))
res = []
for p in m["pairs"]:
    va, vb = m["strips"][p["a"]]["variant"], m["strips"][p["b"]]["variant"]
    better = "a" if rank[va] > rank[vb] else "b"
    w = better if rng.random() < 0.85 else ("tie" if rng.random() < 0.3 else ("b" if better == "a" else "a"))
    res.append(dict(pair_id=p["pair_id"], a=p["a"], b=p["b"], winner=w, ms=rng.randint(800, 4000), theme="dark"))
out = os.path.join(HERE, sys.argv[2] if len(sys.argv) > 2 else "results_synthetic.json")
json.dump(res, open(out, "w"), indent=1)
print("wrote", out, len(res))
