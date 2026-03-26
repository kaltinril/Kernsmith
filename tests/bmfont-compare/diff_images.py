"""Compare PNG texture atlas images between two BMFont output directories.

For each matching font, compares the _0.png files and produces:
- A summary table with pixel dimensions, match %, and diff pixel counts
- Visual diff images (abs difference amplified 4x) saved to an output directory

Usage:
    python diff_images.py [dir_a] [dir_b] [--output-dir diffs/]

Defaults:
    dir_a      = gum-bmfont
    dir_b      = gum-gdi
    output-dir = gum-diffs
"""

import argparse
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow is required: pip install Pillow", file=sys.stderr)
    sys.exit(1)

SCRIPT_DIR = Path(__file__).resolve().parent
TOLERANCE = 5  # per-channel tolerance for antialiasing differences


def find_font_pngs(directory: Path) -> dict[str, Path]:
    """Return a dict of font_name -> path for all _0.png files in directory."""
    result = {}
    for png in sorted(directory.glob("*_0.png")):
        # Strip the _0.png suffix to get the font name
        font_name = png.name[: -len("_0.png")]
        result[font_name] = png
    return result


def compare_images(
    path_a: Path, path_b: Path, tolerance: int = TOLERANCE
) -> tuple[bool, int, int, float, Image.Image | None]:
    """Compare two PNG images.

    Returns:
        (same_size, total_pixels, diff_pixels, match_pct, diff_image_or_None)
    """
    img_a = Image.open(path_a).convert("RGBA")
    img_b = Image.open(path_b).convert("RGBA")

    if img_a.size != img_b.size:
        return False, 0, 0, 0.0, None

    w, h = img_a.size
    total = w * h

    pix_a = img_a.load()
    pix_b = img_b.load()

    diff_img = Image.new("RGB", (w, h), (0, 0, 0))
    diff_pix = diff_img.load()

    diff_count = 0
    for y in range(h):
        for x in range(w):
            ra, ga, ba, aa = pix_a[x, y]
            rb, gb, bb, ab = pix_b[x, y]

            dr = abs(ra - rb)
            dg = abs(ga - gb)
            db = abs(ba - bb)
            da = abs(aa - ab)

            if dr > tolerance or dg > tolerance or db > tolerance or da > tolerance:
                diff_count += 1
                # Amplify the difference for visibility (4x, clamped to 255)
                diff_pix[x, y] = (
                    min(dr * 4, 255),
                    min(dg * 4, 255),
                    min(db * 4, 255),
                )
            else:
                # Matching pixel: show as dark gray so the diff image isn't pure black
                diff_pix[x, y] = (30, 30, 30)

    match_pct = ((total - diff_count) / total * 100) if total > 0 else 100.0
    return True, total, diff_count, match_pct, diff_img


def classify_notes(same_size: bool, diff_pixels: int, match_pct: float) -> str:
    """Return a short human-readable note for the comparison."""
    if not same_size:
        return "SIZE MISMATCH"
    if diff_pixels == 0:
        return "identical"
    if match_pct >= 95.0:
        return "AA differences"
    if match_pct >= 80.0:
        return "moderate diffs"
    return "significant diffs"


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Compare PNG texture atlas images between two BMFont output directories."
    )
    parser.add_argument(
        "dir_a",
        nargs="?",
        default="gum-bmfont",
        help="First directory (default: gum-bmfont)",
    )
    parser.add_argument(
        "dir_b",
        nargs="?",
        default="gum-gdi",
        help="Second directory (default: gum-gdi)",
    )
    parser.add_argument(
        "--output-dir",
        default="gum-diffs",
        help="Directory to save diff images (default: gum-diffs)",
    )
    args = parser.parse_args()

    dir_a = Path(args.dir_a)
    dir_b = Path(args.dir_b)
    out_dir = Path(args.output_dir)

    # Resolve relative paths from script directory
    if not dir_a.is_absolute():
        dir_a = SCRIPT_DIR / dir_a
    if not dir_b.is_absolute():
        dir_b = SCRIPT_DIR / dir_b
    if not out_dir.is_absolute():
        out_dir = SCRIPT_DIR / out_dir

    if not dir_a.is_dir():
        print(f"Error: directory not found: {dir_a}", file=sys.stderr)
        sys.exit(1)
    if not dir_b.is_dir():
        print(f"Error: directory not found: {dir_b}", file=sys.stderr)
        sys.exit(1)

    out_dir.mkdir(parents=True, exist_ok=True)

    fonts_a = find_font_pngs(dir_a)
    fonts_b = find_font_pngs(dir_b)
    all_fonts = sorted(set(fonts_a) | set(fonts_b))

    if not all_fonts:
        print("No _0.png files found in either directory.")
        sys.exit(0)

    # Print header
    print(f"\nComparing: {dir_a.name}  vs  {dir_b.name}")
    print(f"Output:    {out_dir}\n")

    hdr = (
        f"{'Font':<35} | {'Size A':<11} | {'Size B':<11} | {'Match%':>7} "
        f"| {'Diff Pixels':>11} | Notes"
    )
    sep = (
        f"{'-'*35}-+-{'-'*11}-+-{'-'*11}-+-{'-'*7}"
        f"-+-{'-'*11}-+{'-'*20}"
    )
    print(hdr)
    print(sep)

    total_compared = 0
    total_identical = 0
    total_mismatch = 0
    total_missing = 0

    for font in all_fonts:
        pa = fonts_a.get(font)
        pb = fonts_b.get(font)

        if pa is None:
            print(f"{font:<35} | {'MISSING':<11} | {'-':<11} | {'-':>7} | {'-':>11} | only in {dir_b.name}")
            total_missing += 1
            continue
        if pb is None:
            print(f"{font:<35} | {'-':<11} | {'MISSING':<11} | {'-':>7} | {'-':>11} | only in {dir_a.name}")
            total_missing += 1
            continue

        total_compared += 1

        img_a = Image.open(pa)
        img_b = Image.open(pb)
        size_a_str = f"{img_a.size[0]}x{img_a.size[1]}"
        size_b_str = f"{img_b.size[0]}x{img_b.size[1]}"
        img_a.close()
        img_b.close()

        same_size, total_px, diff_px, match_pct, diff_img = compare_images(pa, pb)

        if not same_size:
            notes = "SIZE MISMATCH"
            print(
                f"{font:<35} | {size_a_str:<11} | {size_b_str:<11} | {'-':>7} "
                f"| {'-':>11} | {notes}"
            )
            total_mismatch += 1
            continue

        notes = classify_notes(same_size, diff_px, match_pct)
        match_str = f"{match_pct:.1f}%"
        print(
            f"{font:<35} | {size_a_str:<11} | {size_b_str:<11} | {match_str:>7} "
            f"| {diff_px:>11} | {notes}"
        )

        if diff_px == 0:
            total_identical += 1

        # Save diff image
        if diff_img is not None:
            diff_path = out_dir / f"{font}_diff.png"
            diff_img.save(diff_path)

    # Summary
    print(sep)
    print(f"\nSummary:")
    print(f"  Total fonts found:    {len(all_fonts)}")
    print(f"  Compared (same size): {total_compared - total_mismatch}")
    print(f"  Identical:            {total_identical}")
    print(f"  Size mismatches:      {total_mismatch}")
    print(f"  Missing in one dir:   {total_missing}")

    if total_compared - total_mismatch > 0:
        diff_images_saved = total_compared - total_mismatch
        print(f"  Diff images saved:    {diff_images_saved} in {out_dir}")


if __name__ == "__main__":
    main()
