"""Generate pixel-diff composite images between two sets of comparison PNGs,
and text-diff .fnt metadata files for regression detection.

Compares three categories of files:

1. **Hardcoded comparison PNGs** (comparison.png through comparison4.png) --
   the original 4-config comparison images.

2. **Per-font comparison PNGs** -- auto-discovered comparison-*.png files
   produced per font. In --dir mode, matches main_comparison-*.png to
   comparison-*.png. In --baseline/--current mode, matches comparison-*.png
   across both directories.

3. **FNT metadata files** -- auto-discovered *.fnt files. Parsed as BMFont
   key=value text format. The ``kernsmith`` metadata line is skipped
   (it contains the version + commit hash that always differs between
   branches). All other lines are compared for exact text match.

For PNG comparisons, diff images are produced where:
  - Identical pixels are shown dimmed (1/3 brightness)
  - Different pixels are shown in bright magenta (#FF00FF)

Designed to catch regressions from performance optimizations, refactors, or
any change that should produce bit-identical output.

Usage:
    # Compare two directories:
    python diff_comparisons.py --baseline dir_a --current dir_b --output diffs/

    # Compare main_comparison*.png vs comparison*.png in the same directory:
    python diff_comparisons.py --dir tests/bmfont-compare/output

    # With tolerance (per-channel, for antialiasing):
    python diff_comparisons.py --dir tests/bmfont-compare/output --tolerance 1

Exit code:
    0 = all comparisons identical (PNGs within tolerance, FNTs exact match)
    1 = differences found
    2 = error (missing files, etc.)
"""

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow is required: pip install Pillow", file=sys.stderr)
    sys.exit(2)

COMPARISON_FILES = [
    ("comparison.png", "Fire config (outline + gradient + shadow)"),
    ("comparison2.png", "Plain config"),
    ("comparison3.png", "Bold comparison"),
    ("comparison4.png", "Italic comparison"),
]


def pixel_diff(a: tuple, b: tuple, tolerance: int) -> bool:
    """Returns True if pixels differ beyond tolerance."""
    return any(abs(ac - bc) > tolerance for ac, bc in zip(a, b))


def generate_diff(
    baseline_path: Path,
    current_path: Path,
    output_path: Path,
    label: str,
    tolerance: int = 0,
) -> tuple[int, int]:
    """Generate a diff image. Returns (diff_count, total_pixels)."""
    if not baseline_path.exists():
        print(f"  SKIP {label}: baseline missing ({baseline_path.name})")
        return 0, 0
    if not current_path.exists():
        print(f"  SKIP {label}: current missing ({current_path.name})")
        return 0, 0

    baseline = Image.open(baseline_path).convert("RGBA")
    current = Image.open(current_path).convert("RGBA")

    # Handle size mismatches by padding to the larger size
    if baseline.size != current.size:
        print(f"  WARN {label}: size mismatch baseline={baseline.size} current={current.size}")
        w = max(baseline.width, current.width)
        h = max(baseline.height, current.height)
        new_baseline = Image.new("RGBA", (w, h), (0, 0, 0, 255))
        new_current = Image.new("RGBA", (w, h), (0, 0, 0, 255))
        new_baseline.paste(baseline, (0, 0))
        new_current.paste(current, (0, 0))
        baseline = new_baseline
        current = new_current

    w, h = baseline.size
    base_px = baseline.load()
    curr_px = current.load()

    diff_img = Image.new("RGBA", (w, h))
    diff_px = diff_img.load()

    diff_count = 0
    total = w * h

    for y in range(h):
        for x in range(w):
            bp = base_px[x, y]
            cp = curr_px[x, y]

            if pixel_diff(bp, cp, tolerance):
                diff_px[x, y] = (255, 0, 255, 255)  # Bright magenta
                diff_count += 1
            else:
                # Dimmed original
                diff_px[x, y] = (cp[0] // 3, cp[1] // 3, cp[2] // 3, 255)

    diff_img.save(output_path)

    pct = (diff_count / total) * 100 if total > 0 else 0
    status = "IDENTICAL" if diff_count == 0 else f"DIFFERENT ({diff_count} pixels, {pct:.4f}%)"
    print(f"  {label}: {status}")

    return diff_count, total


def discover_per_font_pngs(baseline_dir: Path, current_dir: Path, prefix: str) -> list[tuple[Path, Path, str]]:
    """Discover per-font comparison PNGs and return (baseline, current, label) tuples."""
    pairs = []
    if prefix:
        # --dir mode: main_comparison-*.png vs comparison-*.png in same dir
        for baseline_path in sorted(baseline_dir.glob(f"{prefix}comparison-*.png")):
            suffix = baseline_path.name[len(prefix):]  # e.g. "comparison-Roboto.png"
            current_path = current_dir / suffix
            label = suffix.replace(".png", "").replace("comparison-", "Per-font: ")
            pairs.append((baseline_path, current_path, label))
    else:
        # --baseline/--current mode: comparison-*.png in both dirs
        for current_path in sorted(current_dir.glob("comparison-*.png")):
            baseline_path = baseline_dir / current_path.name
            label = current_path.name.replace(".png", "").replace("comparison-", "Per-font: ")
            pairs.append((baseline_path, current_path, label))
    return pairs


def compare_fnt_files(baseline_path: Path, current_path: Path) -> tuple[bool, int]:
    """Compare two .fnt files line-by-line, skipping the info line.

    Returns (is_identical, differing_line_count).
    """
    baseline_lines = baseline_path.read_text(encoding="utf-8").splitlines()
    current_lines = current_path.read_text(encoding="utf-8").splitlines()

    # Filter out the "kernsmith " line which contains version + commit hash
    # that always differs between branches
    baseline_filtered = [l for l in baseline_lines if not l.startswith("kernsmith ")]
    current_filtered = [l for l in current_lines if not l.startswith("kernsmith ")]

    diff_count = 0
    max_len = max(len(baseline_filtered), len(current_filtered))
    for i in range(max_len):
        bl = baseline_filtered[i] if i < len(baseline_filtered) else None
        cl = current_filtered[i] if i < len(current_filtered) else None
        if bl != cl:
            diff_count += 1

    return diff_count == 0, diff_count


def discover_fnt_files(baseline_dir: Path, current_dir: Path, prefix: str) -> list[tuple[Path, Path, str]]:
    """Discover .fnt files and return (baseline, current, label) tuples."""
    pairs = []
    if prefix:
        # --dir mode: main_*.fnt vs *.fnt in same dir
        for baseline_path in sorted(baseline_dir.glob(f"{prefix}*.fnt")):
            suffix = baseline_path.name[len(prefix):]  # e.g. "Roboto.fnt"
            current_path = current_dir / suffix
            label = suffix
            pairs.append((baseline_path, current_path, label))
    else:
        # --baseline/--current mode: *.fnt in both dirs
        for current_path in sorted(current_dir.glob("*.fnt")):
            baseline_path = baseline_dir / current_path.name
            label = current_path.name
            pairs.append((baseline_path, current_path, label))
    return pairs


def main():
    parser = argparse.ArgumentParser(description="Pixel-diff comparison PNGs for regression detection")
    parser.add_argument("--dir", type=str, help="Single directory with main_comparison*.png and comparison*.png")
    parser.add_argument("--baseline", type=str, help="Directory containing baseline comparison PNGs")
    parser.add_argument("--current", type=str, help="Directory containing current comparison PNGs")
    parser.add_argument("--output", type=str, help="Output directory for diff images (default: same as --dir or --current)")
    parser.add_argument("--tolerance", type=int, default=0, help="Per-channel tolerance (0 = exact match)")
    parser.add_argument("--prefix", type=str, default="main_", help="Baseline filename prefix (default: main_)")
    args = parser.parse_args()

    if args.dir:
        baseline_dir = Path(args.dir)
        current_dir = Path(args.dir)
        output_dir = Path(args.output) if args.output else Path(args.dir)
        prefix = args.prefix
    elif args.baseline and args.current:
        baseline_dir = Path(args.baseline)
        current_dir = Path(args.current)
        output_dir = Path(args.output) if args.output else current_dir
        prefix = ""
    else:
        parser.error("Provide either --dir or both --baseline and --current")
        return

    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Baseline: {baseline_dir}")
    print(f"Current:  {current_dir}")
    print(f"Output:   {output_dir}")
    print(f"Tolerance: {args.tolerance} per channel")
    print()

    any_differences = False

    # ── Section 1: Hardcoded comparison PNGs ──────────────────────────
    total_diffs = 0
    total_pixels = 0
    files_compared = 0

    for filename, label in COMPARISON_FILES:
        baseline_name = f"{prefix}{filename}" if prefix else filename
        baseline_path = baseline_dir / baseline_name
        current_path = current_dir / filename
        diff_name = f"diff_{filename}"
        output_path = output_dir / diff_name

        diffs, pixels = generate_diff(baseline_path, current_path, output_path, label, args.tolerance)
        if pixels > 0:
            total_diffs += diffs
            total_pixels += pixels
            files_compared += 1

    print()
    if files_compared == 0:
        print("No hardcoded comparison files found.")
    elif total_diffs == 0:
        print(f"PASS: All {files_compared} hardcoded comparisons are pixel-identical.")
    else:
        pct = (total_diffs / total_pixels) * 100
        print(f"FAIL: {total_diffs} different pixels across {files_compared} hardcoded comparisons ({pct:.4f}%)")
        any_differences = True

    # ── Section 2: Per-font comparison PNGs ───────────────────────────
    per_font_pairs = discover_per_font_pngs(baseline_dir, current_dir, prefix)

    if per_font_pairs:
        print()
        print("Per-font comparisons")
        print("-" * 40)
        pf_diffs = 0
        pf_pixels = 0
        pf_compared = 0

        for baseline_path, current_path, label in per_font_pairs:
            diff_name = f"diff_{current_path.name}"
            output_path = output_dir / diff_name
            diffs, pixels = generate_diff(baseline_path, current_path, output_path, label, args.tolerance)
            if pixels > 0:
                pf_diffs += diffs
                pf_pixels += pixels
                pf_compared += 1

        print()
        if pf_compared == 0:
            print("No per-font comparison files compared.")
        elif pf_diffs == 0:
            print(f"PASS: All {pf_compared} per-font comparisons are pixel-identical.")
        else:
            pct = (pf_diffs / pf_pixels) * 100
            print(f"FAIL: {pf_diffs} different pixels across {pf_compared} per-font comparisons ({pct:.4f}%)")
            any_differences = True

    # ── Section 3: FNT metadata comparisons ───────────────────────────
    fnt_pairs = discover_fnt_files(baseline_dir, current_dir, prefix)

    if fnt_pairs:
        print()
        print("FNT metadata comparisons")
        print("-" * 40)
        fnt_identical = 0
        fnt_different = 0
        fnt_skipped = 0

        for baseline_path, current_path, label in fnt_pairs:
            if not baseline_path.exists():
                print(f"  SKIP {label}: baseline missing ({baseline_path.name})")
                fnt_skipped += 1
                continue
            if not current_path.exists():
                print(f"  SKIP {label}: current missing ({current_path.name})")
                fnt_skipped += 1
                continue

            is_identical, diff_lines = compare_fnt_files(baseline_path, current_path)
            if is_identical:
                print(f"  {label}: IDENTICAL")
                fnt_identical += 1
            else:
                print(f"  {label}: DIFFERENT ({diff_lines} lines differ)")
                fnt_different += 1

        print()
        total_fnt = fnt_identical + fnt_different
        if total_fnt == 0:
            print("No FNT files compared.")
        elif fnt_different == 0:
            print(f"PASS: All {fnt_identical} FNT files are identical (excluding kernsmith version line).")
        else:
            print(f"FAIL: {fnt_different} of {total_fnt} FNT files differ.")
            any_differences = True

    # ── Final result ──────────────────────────────────────────────────
    total_compared = files_compared + len(per_font_pairs) + len(fnt_pairs)
    if total_compared == 0:
        print()
        print("No files compared.")
        sys.exit(2)

    print()
    if any_differences:
        print("RESULT: Differences detected.")
        sys.exit(1)
    else:
        print("RESULT: All comparisons passed.")
        sys.exit(0)


if __name__ == "__main__":
    main()
