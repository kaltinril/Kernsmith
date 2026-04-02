"""Generate pixel-diff composite images between two sets of comparison PNGs.

Compares comparison{N}.png files from two directories (or a baseline set vs
current) and produces diff images where:
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
    0 = all images identical (within tolerance)
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
        print("No files compared.")
        sys.exit(2)

    if total_diffs == 0:
        print(f"PASS: All {files_compared} comparisons are pixel-identical.")
        sys.exit(0)
    else:
        pct = (total_diffs / total_pixels) * 100
        print(f"FAIL: {total_diffs} different pixels across {files_compared} comparisons ({pct:.4f}%)")
        sys.exit(1)


if __name__ == "__main__":
    main()
