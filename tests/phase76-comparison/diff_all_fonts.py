"""Compare matching .fnt files between BMFont and KernSmith output directories."""

import os
import re
import sys
from pathlib import Path

DIR_A = Path(r"c:/git/kernsmith/tests/phase76-comparison/gum-bmfont")
DIR_B = Path(r"c:/git/kernsmith/tests/phase76-comparison/gum-kernsmith")

LABEL_A = "BMFont"
LABEL_B = "KernSmith"


def parse_kv(line: str) -> dict[str, str]:
    """Parse key=value pairs from a BMFont text line, handling quoted values."""
    result = {}
    for m in re.finditer(r'(\w+)=("[^"]*"|\S+)', line):
        k, v = m.group(1), m.group(2).strip('"')
        result[k] = v
    return result


def parse_fnt(path: Path) -> dict:
    """Parse a .fnt file into info, common, chars dict, and kernings dict."""
    info = {}
    common = {}
    chars = {}  # id -> {xadvance, xoffset, yoffset, width, height}
    kernings = {}  # (first, second) -> amount

    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("info "):
            info = parse_kv(line)
        elif line.startswith("common "):
            common = parse_kv(line)
        elif line.startswith("char "):
            kv = parse_kv(line)
            cid = int(kv["id"])
            chars[cid] = {
                "xadvance": int(kv.get("xadvance", 0)),
                "xoffset": int(kv.get("xoffset", 0)),
                "yoffset": int(kv.get("yoffset", 0)),
                "width": int(kv.get("width", 0)),
                "height": int(kv.get("height", 0)),
            }
        elif line.startswith("kerning "):
            kv = parse_kv(line)
            pair = (int(kv["first"]), int(kv["second"]))
            kernings[pair] = int(kv["amount"])

    return {"info": info, "common": common, "chars": chars, "kernings": kernings}


def compare(name: str, fa: dict, fb: dict) -> None:
    """Print a compact comparison summary for one font."""
    ia, ib = fa["info"], fb["info"]
    ca, cb = fa["common"], fb["common"]

    face = ia.get("face", "?")
    size_a = ia.get("size", "?")
    size_b = ib.get("size", "?")
    lh_a, lh_b = int(ca.get("lineHeight", 0)), int(cb.get("lineHeight", 0))
    base_a, base_b = int(ca.get("base", 0)), int(cb.get("base", 0))

    print(f"--- {name}  face={face}  size={size_a}/{size_b} ---")
    print(f"  lineHeight: {lh_a} vs {lh_b}  delta={lh_b - lh_a}")
    print(f"  base:       {base_a} vs {base_b}  delta={base_b - base_a}")

    # Character metrics
    chars_a, chars_b = fa["chars"], fb["chars"]
    shared_ids = sorted(set(chars_a) & set(chars_b))
    only_a = set(chars_a) - set(chars_b)
    only_b = set(chars_b) - set(chars_a)

    for field in ("xadvance", "xoffset", "yoffset"):
        deltas = []
        for cid in shared_ids:
            d = chars_b[cid][field] - chars_a[cid][field]
            if d != 0:
                deltas.append(d)
        cnt = len(deltas)
        if cnt == 0:
            print(f"  {field:9s}: all match ({len(shared_ids)} chars)")
        else:
            avg = sum(deltas) / cnt
            max_abs = max(abs(d) for d in deltas)
            print(f"  {field:9s}: {cnt}/{len(shared_ids)} differ  avg={avg:+.2f}  max|d|={max_abs}")

    if only_a or only_b:
        print(f"  chars only in {LABEL_A}: {len(only_a)}  only in {LABEL_B}: {len(only_b)}")

    # Kerning
    ka, kb = fa["kernings"], fb["kernings"]
    shared_pairs = set(ka) & set(kb)
    only_ka = set(ka) - set(kb)
    only_kb = set(kb) - set(ka)
    diff_amt = sum(1 for p in shared_pairs if ka[p] != kb[p])

    if not ka and not kb:
        print(f"  kerning:    none in either file")
    else:
        print(
            f"  kerning:    shared={len(shared_pairs)}  diff_amount={diff_amt}  "
            f"only_{LABEL_A}={len(only_ka)}  only_{LABEL_B}={len(only_kb)}"
        )

    print()


def main() -> None:
    fnt_a = {f.name for f in DIR_A.glob("*.fnt")}
    fnt_b = {f.name for f in DIR_B.glob("*.fnt")}
    matched = sorted(fnt_a & fnt_b)
    only_a = sorted(fnt_a - fnt_b)
    only_b = sorted(fnt_b - fnt_a)

    print(f"Comparing {len(matched)} matched .fnt files\n")

    for name in matched:
        fa = parse_fnt(DIR_A / name)
        fb = parse_fnt(DIR_B / name)
        compare(name, fa, fb)

    if only_a:
        print(f"=== Only in {LABEL_A} ({len(only_a)}) ===")
        for n in only_a:
            print(f"  {n}")
        print()

    if only_b:
        print(f"=== Only in {LABEL_B} ({len(only_b)}) ===")
        for n in only_b:
            print(f"  {n}")
        print()


if __name__ == "__main__":
    main()
