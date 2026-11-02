#!/bin/bash
# Round-2 render matrix from the CURRENT main-tree CLI build (copied to ../cli2).
# Variants: ft = freetype, gdi = gdi (none only), nd = native --hint-level none, nn = native (default = Normal).
SP="${KERNSMITH_RESEARCH_OUT:-output/research}"
CLI="$SP/cli2/KernSmith.Cli.exe"
OUT="$SP/pairwise2/renders"
mkdir -p "$OUT"
declare -A FONTS=(
  [roboto]="C:/git/kernsmith/tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf"
  [verdana]="C:/Windows/Fonts/verdana.ttf"
  [georgia]="C:/Windows/Fonts/georgia.ttf"
)
SIZES="${SIZES:-10 11 12}"
for f in roboto verdana georgia; do
  for s in $SIZES; do
    for aa in grayscale none; do
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer freetype --aa $aa --charset latin -o "$OUT/${f}_${s}_ft_${aa}" >/dev/null 2>&1 || echo "FAIL ft $f $s $aa"
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --aa $aa --charset latin -o "$OUT/${f}_${s}_nn_${aa}" >/dev/null 2>&1 || echo "FAIL nn $f $s $aa"
      "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --hint-level none --aa $aa --charset latin -o "$OUT/${f}_${s}_nd_${aa}" >/dev/null 2>&1 || echo "FAIL nd $f $s $aa"
    done
    "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer gdi --aa none --charset latin -o "$OUT/${f}_${s}_gdi_none" >/dev/null 2>&1 || echo "FAIL gdi $f $s"
  done
done
echo DONE
