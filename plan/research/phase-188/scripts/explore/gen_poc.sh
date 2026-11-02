#!/bin/bash
# PoC variants: ni = Normal at integer ppem; nx = integer ppem + nearest x-height rounding
WT="${KERNSMITH_ROOT:-.}"
CLI="$WT/tools/KernSmith.Cli/bin/Debug/net10.0-windows/KernSmith.Cli.exe"
OUT="${KERNSMITH_RESEARCH_OUT:-output/research}/renders"
declare -A FONTS=(
  [roboto]="$WT/tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf"
  [verdana]="C:/Windows/Fonts/verdana.ttf"
  [tahoma]="C:/Windows/Fonts/tahoma.ttf"
  [georgia]="C:/Windows/Fonts/georgia.ttf"
  [arial]="C:/Windows/Fonts/arial.ttf"
)
for f in "${!FONTS[@]}"; do
  for s in 7 8 9 10 11 12 13 14 16 20; do
    for aa in grayscale none; do
      KERNSMITH_POC_INTPPEM=1 "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --hint-level normal --aa $aa --charset latin -o "$OUT/${f}_${s}_ni_${aa}" >/dev/null 2>&1 || echo "FAIL ni $f $s $aa"
      KERNSMITH_POC_INTPPEM=1 KERNSMITH_POC_XHNEAREST=1 "$CLI" generate -f "${FONTS[$f]}" -s $s --rasterizer native --hint-level normal --aa $aa --charset latin -o "$OUT/${f}_${s}_nx_${aa}" >/dev/null 2>&1 || echo "FAIL nx $f $s $aa"
    done
  done
done
echo DONE
