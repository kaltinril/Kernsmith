#!/bin/bash
# Baseline render matrix: fonts x sizes x {freetype full, native normal, native default} x {grayscale, none}
WT="${KERNSMITH_ROOT:-.}"
CLI="$WT/tools/KernSmith.Cli/bin/Debug/net10.0-windows/KernSmith.Cli.exe"
OUT="${KERNSMITH_RESEARCH_OUT:-output/research}/renders"
mkdir -p "$OUT"
declare -A FONTS=(
  [roboto]="$WT/tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf"
  [verdana]="C:/Windows/Fonts/verdana.ttf"
  [tahoma]="C:/Windows/Fonts/tahoma.ttf"
  [georgia]="C:/Windows/Fonts/georgia.ttf"
  [arial]="C:/Windows/Fonts/arial.ttf"
  [notosans]="$WT/tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin.ttf"
  [sourceserif]="$WT/tests/KernSmith.Tests/Fixtures/SourceSerif4-Regular.otf"
)
SIZES="${SIZES:-7 8 9 10 11 12 13 14 16 20}"
for f in "${!FONTS[@]}"; do
  for s in $SIZES; do
    for aa in grayscale none; do
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer freetype --aa $aa --charset latin -o "$OUT/${f}_${s}_ft_${aa}" >/dev/null 2>&1 || echo "FAIL ft $f $s $aa"
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --hint-level normal --aa $aa --charset latin -o "$OUT/${f}_${s}_nn_${aa}" >/dev/null 2>&1 || echo "FAIL nn $f $s $aa"
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --aa $aa --charset latin -o "$OUT/${f}_${s}_nd_${aa}" >/dev/null 2>&1 || echo "FAIL nd $f $s $aa"
    done
    "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer gdi --aa none --charset latin -o "$OUT/${f}_${s}_gdi_none" >/dev/null 2>&1 || echo "FAIL gdi $f $s"
    "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer freetype --no-hinting --aa grayscale --charset latin -o "$OUT/${f}_${s}_ftu_grayscale" >/dev/null 2>&1 || echo "FAIL ftu $f $s"
  done
done
echo DONE
