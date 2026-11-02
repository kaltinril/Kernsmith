"""Direction A: what gasp says, decoded, over the system fonts: the ppem below which designers say 'do not grid-fit',
and the band they mark grid-fit-without-smoothing (bilevel)."""
import json, os, collections
here = os.path.dirname(os.path.abspath(__file__))
rows = json.load(open(os.path.join(here, "survey_tables.json")))
GRIDFIT, DOGRAY, SYMGRID, SYMSMOOTH = 1, 2, 4, 8
nogrid_upper = collections.Counter(); bilevel_bands = collections.Counter(); kinds = collections.Counter()
for r in rows:
    g = r.get("gasp_ranges")
    if not isinstance(g, dict): continue
    items = sorted((int(k), v) for k, v in g.items())
    kinds[tuple(items)] += 1
    # first range with GRIDFIT off
    lo = 0; first_nogrid = None; bilevel = []
    for upper, v in items:
        if not (v & GRIDFIT) and first_nogrid is None and lo == 0: first_nogrid = upper
        if (v & GRIDFIT) and not (v & DOGRAY): bilevel.append((lo + 1, upper))
        lo = upper
    nogrid_upper[first_nogrid] += 1
    for b in bilevel: bilevel_bands[b] += 1
print("fonts with gasp:", sum(kinds.values()))
print("'no grid-fit up to ppem N' (None = grid-fit from 1 ppem):", dict(sorted(nogrid_upper.items(), key=lambda kv: (kv[0] is None, kv[0]))))
print("bilevel (gridfit, no gray) bands:", bilevel_bands.most_common(8))
print("most common gasp tables:")
for k, n in kinds.most_common(8): print(f"  {n:4d}  {k}")
