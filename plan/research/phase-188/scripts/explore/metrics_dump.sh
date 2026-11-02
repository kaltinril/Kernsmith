#!/bin/bash
WT="${KERNSMITH_ROOT:-.}"
CLI="$WT/tools/KernSmith.Cli/bin/Debug/net10.0-windows/KernSmith.Cli.exe"
OUT="${KERNSMITH_RESEARCH_OUT:-output/research}/metrics"
mkdir -p "$OUT"
for f in "$WT/tests/KernSmith.Tests/Fixtures/SourceSerif4-Regular.otf" "$WT/tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin-CFF.otf" "$WT/tests/KernSmith.Tests/Fixtures/NotoSans-Regular-Latin.ttf" "C:/Windows/Fonts/arial.ttf" "C:/Windows/Fonts/verdana.ttf" "C:/Windows/Fonts/georgia.ttf" "C:/Windows/Fonts/tahoma.ttf" "$WT/tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf"; do
  echo "== $(basename "$f")"
  for s in 10 12 16; do
    KERNSMITH_POC_METRICS=1 "$CLI" generate -f "$f" -s $s --rasterizer native --hint-level normal --chars "Hx" -o "$OUT/m" 2>&1 >/dev/null | grep "PoC metrics" | head -1
  done
done
