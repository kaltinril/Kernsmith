"""Compare two BMFont .fnt text files and print metric differences."""

import re
import sys
from pathlib import Path

BMFONT_PATH = Path(__file__).parent / "bmfont" / "compare.fnt"
KERNSMITH_PATH = Path(__file__).parent / "kernsmith" / "compare.fnt"


def parse_attrs(line):
    """Parse key=value pairs from a .fnt line, handling quoted values."""
    attrs = {}
    for m in re.finditer(r'(\w+)=("[^"]*"|\S+)', line):
        key, val = m.group(1), m.group(2).strip('"')
        try:
            attrs[key] = int(val)
        except ValueError:
            attrs[key] = val
    return attrs


def parse_fnt(path):
    """Parse a .fnt text file into info, common, chars dict, and kernings dict."""
    info = {}
    common = {}
    chars = {}  # id -> attrs
    kernings = {}  # (first, second) -> amount

    for line in Path(path).read_text().splitlines():
        line = line.strip()
        if line.startswith("info "):
            info = parse_attrs(line)
        elif line.startswith("common "):
            common = parse_attrs(line)
        elif line.startswith("char "):
            attrs = parse_attrs(line)
            chars[attrs["id"]] = attrs
        elif line.startswith("kerning "):
            attrs = parse_attrs(line)
            kernings[(attrs["first"], attrs["second"])] = attrs["amount"]

    return info, common, chars, kernings


def chr_label(cid):
    """Return a printable label for a character id."""
    if 33 <= cid <= 126:
        return f"'{chr(cid)}'"
    return f"U+{cid:04X}"


def main():
    bm_info, bm_common, bm_chars, bm_kerns = parse_fnt(BMFONT_PATH)
    ks_info, ks_common, ks_chars, ks_kerns = parse_fnt(KERNSMITH_PATH)

    # --- Common line ---
    print("=" * 70)
    print("COMMON LINE COMPARISON")
    print("=" * 70)
    common_keys = ["lineHeight", "base", "scaleW", "scaleH", "pages", "packed"]
    print(f"  {'Field':<14} {'BMFont':>8} {'KernSmith':>10} {'Delta':>8}")
    print(f"  {'-'*14} {'-'*8} {'-'*10} {'-'*8}")
    for key in common_keys:
        bv = bm_common.get(key, "N/A")
        kv = ks_common.get(key, "N/A")
        delta = ""
        if isinstance(bv, int) and isinstance(kv, int):
            d = kv - bv
            delta = f"{d:+d}" if d != 0 else "0"
        print(f"  {key:<14} {str(bv):>8} {str(kv):>10} {delta:>8}")

    # --- Per-character metrics ---
    print()
    print("=" * 70)
    print("PER-CHARACTER METRIC DIFFERENCES")
    print("=" * 70)

    all_ids = sorted(set(bm_chars.keys()) | set(ks_chars.keys()))
    fields = ["xadvance", "xoffset", "yoffset"]

    header = f"  {'ID':>5} {'Char':<6}"
    for f in fields:
        header += f" {'BM':>4} {'KS':>4} {'d':>4}"
    print(header)
    print(f"  {'-'*5} {'-'*6}" + (f" {'-'*4} {'-'*4} {'-'*4}" * len(fields)))

    diff_count = 0
    xadv_deltas = []

    for cid in all_ids:
        bm = bm_chars.get(cid)
        ks = ks_chars.get(cid)
        if bm is None or ks is None:
            label = chr_label(cid)
            src = "BMFont only" if bm else "KernSmith only"
            print(f"  {cid:>5} {label:<6} ** {src} **")
            diff_count += 1
            continue

        row_diffs = False
        cols = []
        for f in fields:
            bv = bm[f]
            kv = ks[f]
            d = kv - bv
            cols.append((bv, kv, d))
            if d != 0:
                row_diffs = True

        xadv_d = cols[0][2]
        xadv_deltas.append(xadv_d)

        if row_diffs:
            diff_count += 1

        line = f"  {cid:>5} {chr_label(cid):<6}"
        for bv, kv, d in cols:
            d_str = f"{d:+d}" if d != 0 else "."
            line += f" {bv:>4} {kv:>4} {d_str:>4}"
        if row_diffs:
            line += "  *"
        print(line)

    # --- Summary stats ---
    print()
    print("=" * 70)
    print("SUMMARY STATISTICS")
    print("=" * 70)
    print(f"  Total characters compared: {len(all_ids)}")
    print(f"  Characters with differences: {diff_count}")
    if xadv_deltas:
        avg = sum(xadv_deltas) / len(xadv_deltas)
        abs_deltas = [abs(d) for d in xadv_deltas]
        max_d = max(abs_deltas)
        nonzero = sum(1 for d in xadv_deltas if d != 0)
        print(f"  xadvance: avg delta = {avg:+.2f}, max |delta| = {max_d}, "
              f"chars with nonzero delta = {nonzero}")

    # --- Kerning pairs ---
    print()
    print("=" * 70)
    print("KERNING PAIR DIFFERENCES")
    print("=" * 70)

    all_pairs = sorted(set(bm_kerns.keys()) | set(ks_kerns.keys()))
    bm_only = []
    ks_only = []
    amount_diffs = []

    for pair in all_pairs:
        bv = bm_kerns.get(pair)
        kv = ks_kerns.get(pair)
        if bv is not None and kv is None:
            bm_only.append((pair, bv))
        elif bv is None and kv is not None:
            ks_only.append((pair, kv))
        elif bv != kv:
            amount_diffs.append((pair, bv, kv))

    if amount_diffs:
        print(f"\n  Pairs with different amounts ({len(amount_diffs)}):")
        print(f"  {'First':>7} {'Second':>7}  {'1st':>5} {'2nd':>5}  {'BM':>4} {'KS':>4} {'Delta':>6}")
        print(f"  {'-'*7} {'-'*7}  {'-'*5} {'-'*5}  {'-'*4} {'-'*4} {'-'*6}")
        for (f, s), bv, kv in amount_diffs:
            d = kv - bv
            print(f"  {f:>7} {s:>7}  {chr_label(f):>5} {chr_label(s):>5}  {bv:>4} {kv:>4} {d:>+5d}")

    if bm_only:
        print(f"\n  Pairs only in BMFont ({len(bm_only)}):")
        for (f, s), amt in bm_only:
            print(f"    {chr_label(f):>5} -> {chr_label(s):<5}  amount={amt}")

    if ks_only:
        print(f"\n  Pairs only in KernSmith ({len(ks_only)}):")
        for (f, s), amt in ks_only:
            print(f"    {chr_label(f):>5} -> {chr_label(s):<5}  amount={amt}")

    if not amount_diffs and not bm_only and not ks_only:
        print("  All kerning pairs match!")

    total_kern_diffs = len(amount_diffs) + len(bm_only) + len(ks_only)
    print(f"\n  Total kerning pairs: BMFont={len(bm_kerns)}, KernSmith={len(ks_kerns)}")
    print(f"  Matching pairs: {len(all_pairs) - total_kern_diffs}")
    print(f"  Different amount: {len(amount_diffs)}")
    print(f"  BMFont only: {len(bm_only)}")
    print(f"  KernSmith only: {len(ks_only)}")


if __name__ == "__main__":
    main()
