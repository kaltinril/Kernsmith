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
   key=value text format. Only the ``version``/``commit`` tokens of the
   ``kernsmith`` line are ignored (they always differ between branches);
   the rest of that line (extended metadata such as ``sdfSpread``) and all
   other lines are compared for exact text match.

Every file is matched across both sides by name. A file present on only one side
is never silently skipped:

  - only on the current side  -> ``NEW (no baseline)``: reported, not a failure
    (a config added on the branch, or a backend that now supports a config).
  - only on the baseline side -> ``MISSING``: counted as a difference, because the
    base produced output that the branch no longer does (a (config, backend) pair
    that now fails) -- unless its config was passed via ``--removed-config``, in
    which case it is reported as ``REMOVED`` and not a failure.

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

    # Configs deleted on the branch (their baseline-only files are REMOVED, not MISSING):
    python diff_comparisons.py --dir tests/bmfont-compare/output --removed-config old-config

Exit code:
    0 = every file present on both sides is identical (PNGs within tolerance, FNTs
        exact match); NEW and REMOVED files do not fail the run
    1 = differences found, or a file MISSING on the current side
    2 = error (nothing could be compared, etc.)
"""

import argparse
import re
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Pillow is required: pip install Pillow", file=sys.stderr)
    sys.exit(2)

# (file, label, source .bmfc config) -- the config decides whether a missing file is REMOVED.
COMPARISON_FILES = [
    ("comparison.png", "Fire config (outline + gradient + shadow)", "fire"),
    ("comparison2.png", "Plain config", "plain"),
    ("comparison3.png", "Bold comparison", "plain"),
    ("comparison4.png", "Italic comparison", "plain"),
]

# Backend suffixes GenerateAll writes: {config}-{backend}.fnt / {config}-{backend}_0.png.
KNOWN_BACKENDS = ("freetype", "gdi", "directwrite", "stbtruetype", "native", "bmfont")


def config_of_fnt(stem: str) -> str:
    """The .bmfc config an output stem belongs to ("plain-native" -> "plain")."""
    for backend in KNOWN_BACKENDS:
        if stem.endswith("-" + backend):
            return stem[: -len(backend) - 1]
    return stem


class Presence:
    """Tracks files that exist on only one side of the comparison."""

    def __init__(self, removed_configs):
        self.removed_configs = set(removed_configs or [])
        self.new = []
        self.missing = []
        self.removed = []

    def check(self, baseline_path: Path, current_path: Path, label: str, config: str) -> bool:
        """Report a one-sided pair. Returns True when both files exist and should be compared."""
        has_base = baseline_path.exists()
        has_curr = current_path.exists()
        if has_base and has_curr:
            return True
        if has_curr:
            print(f"  {label}: NEW (no baseline)")
            self.new.append(label)
        elif has_base and config in self.removed_configs:
            print(f"  {label}: REMOVED (config no longer exists)")
            self.removed.append(label)
        elif has_base:
            print(f"  {label}: MISSING (baseline has output, current does not)")
            self.missing.append(label)
        else:
            print(f"  SKIP {label}: not produced on either side")
        return False


def pixel_diff(a: tuple, b: tuple, tolerance: int) -> bool:
    """Returns True if pixels differ beyond tolerance."""
    return any(abs(ac - bc) > tolerance for ac, bc in zip(a, b))


def generate_diff(
    baseline_path: Path,
    current_path: Path,
    output_path: Path,
    label: str,
    tolerance: int = 0,
    ignore_top: int = 0,
) -> tuple[int, int]:
    """Generate a diff image. Returns (diff_count, total_pixels).

    Rows above ``ignore_top`` (the header label band) are always dimmed and never
    counted as differences -- those rows render the column/config labels with
    GDI text antialiasing that varies run-to-run and is not a font-output change.
    """
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

            if y >= ignore_top and pixel_diff(bp, cp, tolerance):
                diff_px[x, y] = (255, 0, 255, 255)  # Bright magenta
                diff_count += 1
            else:
                # Dimmed original (header band y<ignore_top is always ignored)
                diff_px[x, y] = (cp[0] // 3, cp[1] // 3, cp[2] // 3, 255)

    diff_img.save(output_path)

    pct = (diff_count / total) * 100 if total > 0 else 0
    status = "IDENTICAL" if diff_count == 0 else f"DIFFERENT ({diff_count} pixels, {pct:.4f}%)"
    print(f"  {label}: {status}")

    return diff_count, total


def discover_per_font_pngs(baseline_dir: Path, current_dir: Path, prefix: str) -> list[tuple[Path, Path, str, str]]:
    """Discover per-font comparison PNGs on EITHER side; returns (baseline, current, label, config)."""
    names = {p.name[len(prefix):] for p in baseline_dir.glob(f"{prefix}comparison-*.png")}
    names |= {p.name for p in current_dir.glob("comparison-*.png")}
    pairs = []
    for name in sorted(names):  # e.g. "comparison-Roboto.png"
        config = name[len("comparison-"):-len(".png")]
        pairs.append((baseline_dir / f"{prefix}{name}", current_dir / name, f"Per-font: {config}", config))
    return pairs


# The generator version (and a commit, if present) always differs between branches; the
# rest of the kernsmith line is extended metadata (sdfSpread, outlineThickness, ...) and
# must be compared.
_VOLATILE_KERNSMITH_TOKEN = re.compile(r'\s(?:version|commit)=(?:"[^"]*"|\S*)')


def normalize_fnt_line(line: str) -> str:
    """Drop the volatile version/commit tokens from the ``kernsmith`` line; other lines pass through."""
    if line.startswith("kernsmith"):
        return _VOLATILE_KERNSMITH_TOKEN.sub("", line)
    return line


def compare_fnt_files(baseline_path: Path, current_path: Path) -> tuple[bool, int]:
    """Compare two .fnt files line-by-line, ignoring only the kernsmith version/commit tokens.

    Returns (is_identical, differing_line_count).
    """
    baseline_lines = baseline_path.read_text(encoding="utf-8").splitlines()
    current_lines = current_path.read_text(encoding="utf-8").splitlines()

    baseline_filtered = [normalize_fnt_line(l) for l in baseline_lines]
    current_filtered = [normalize_fnt_line(l) for l in current_lines]

    diff_count = 0
    max_len = max(len(baseline_filtered), len(current_filtered))
    for i in range(max_len):
        bl = baseline_filtered[i] if i < len(baseline_filtered) else None
        cl = current_filtered[i] if i < len(current_filtered) else None
        if bl != cl:
            diff_count += 1

    return diff_count == 0, diff_count


def discover_fnt_files(baseline_dir: Path, current_dir: Path, prefix: str) -> list[tuple[Path, Path, str, str]]:
    """Discover .fnt files on EITHER side; returns (baseline, current, label, config)."""
    names = {p.name[len(prefix):] for p in baseline_dir.glob(f"{prefix}*.fnt")}
    names |= {p.name for p in current_dir.glob("*.fnt") if not (prefix and p.name.startswith(prefix))}
    return [(baseline_dir / f"{prefix}{name}", current_dir / name, name, config_of_fnt(name[:-len(".fnt")]))
            for name in sorted(names)]


def main():
    parser = argparse.ArgumentParser(description="Pixel-diff comparison PNGs for regression detection")
    parser.add_argument("--dir", type=str, help="Single directory with main_comparison*.png and comparison*.png")
    parser.add_argument("--baseline", type=str, help="Directory containing baseline comparison PNGs")
    parser.add_argument("--current", type=str, help="Directory containing current comparison PNGs")
    parser.add_argument("--output", type=str, help="Output directory for diff images (default: same as --dir or --current)")
    parser.add_argument("--tolerance", type=int, default=0, help="Per-channel tolerance (0 = exact match)")
    parser.add_argument("--ignore-top", type=int, default=41,
                        help="Ignore the top N rows of each comparison image -- the column/config "
                             "header label band (incl. its antialiased bottom edge), which renders "
                             "with non-deterministic GDI text antialiasing and is not a font-output "
                             "change (default: 41; use 0 for full image)")
    parser.add_argument("--prefix", type=str, default="main_", help="Baseline filename prefix (default: main_)")
    parser.add_argument("--removed-config", action="append", default=[], metavar="NAME",
                        help="A .bmfc config deleted on the current side: its baseline-only files are "
                             "reported as REMOVED instead of MISSING (repeatable)")
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
    print(f"Ignore top: {args.ignore_top} rows (header label band)")
    print()

    any_differences = False
    presence = Presence(args.removed_config)

    # ── Section 1: Hardcoded comparison PNGs ──────────────────────────
    total_diffs = 0
    total_pixels = 0
    files_compared = 0

    for filename, label, config in COMPARISON_FILES:
        baseline_name = f"{prefix}{filename}" if prefix else filename
        baseline_path = baseline_dir / baseline_name
        current_path = current_dir / filename
        diff_name = f"diff_{filename}"
        output_path = output_dir / diff_name

        if not presence.check(baseline_path, current_path, label, config):
            continue
        diffs, pixels = generate_diff(baseline_path, current_path, output_path, label, args.tolerance, args.ignore_top)
        if pixels > 0:
            total_diffs += diffs
            total_pixels += pixels
            files_compared += 1

    print()
    if files_compared == 0:
        print("No hardcoded comparison files compared.")
    elif total_diffs == 0:
        print(f"PASS: All {files_compared} hardcoded comparisons are pixel-identical.")
    else:
        pct = (total_diffs / total_pixels) * 100
        print(f"FAIL: {total_diffs} different pixels across {files_compared} hardcoded comparisons ({pct:.4f}%)")
        any_differences = True

    # ── Section 2: Per-font comparison PNGs ───────────────────────────
    per_font_pairs = discover_per_font_pngs(baseline_dir, current_dir, prefix)
    pf_compared = 0

    if per_font_pairs:
        print()
        print("Per-font comparisons")
        print("-" * 40)
        pf_diffs = 0
        pf_pixels = 0

        for baseline_path, current_path, label, config in per_font_pairs:
            if not presence.check(baseline_path, current_path, label, config):
                continue
            diff_name = f"diff_{current_path.name}"
            output_path = output_dir / diff_name
            diffs, pixels = generate_diff(baseline_path, current_path, output_path, label, args.tolerance, args.ignore_top)
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
    total_fnt = 0

    if fnt_pairs:
        print()
        print("FNT metadata comparisons")
        print("-" * 40)
        fnt_identical = 0
        fnt_different = 0

        for baseline_path, current_path, label, config in fnt_pairs:
            if not presence.check(baseline_path, current_path, label, config):
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
            print(f"PASS: All {fnt_identical} FNT files are identical (ignoring the kernsmith version token).")
        else:
            print(f"FAIL: {fnt_different} of {total_fnt} FNT files differ.")
            any_differences = True

    # ── Section 4: files present on only one side ─────────────────────
    if presence.new or presence.removed or presence.missing:
        print()
        print("One-sided files")
        print("-" * 40)
        if presence.new:
            print(f"NEW (no baseline, not a failure): {len(presence.new)} file(s)")
        if presence.removed:
            print(f"REMOVED (config deleted, not a failure): {len(presence.removed)} file(s)")
        if presence.missing:
            print(f"FAIL: {len(presence.missing)} file(s) MISSING on the current side "
                  f"(the baseline produced them): {', '.join(presence.missing)}")
            any_differences = True

    # ── Final result ──────────────────────────────────────────────────
    total_compared = files_compared + pf_compared + total_fnt
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
