# Phase 78BB -- GDI BMFont Parity Fixes

> **Status**: In Progress (paused — awaiting REF-08 BMFont source analysis)
> **Size**: Medium
> **Created**: 2026-03-25
> **Updated**: 2026-03-26
> **Dependencies**: Phase 78B (GDI backend must exist)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Close the metrics gap between KernSmith's GDI backend and BMFont's GDI output, achieving exact (or near-exact) parity on lineHeight, base, xadvance, xoffset, yoffset, and kerning pairs.

---

## Problem

Phase 78B validation comparing KernSmith GDI output against BMFont reference output (15 Gum UI fonts) revealed systematic metrics divergence. After first-pass fixes, current state:

### Current Parity Results (15 fonts)

| Category | Fonts | lineHeight | base | xadvance | xoffset | yoffset |
|----------|-------|-----------|------|----------|---------|---------|
| **Exact match** | Arial (18, 24), Bahnschrift (12, 24, 36), Bauhaus 93 (32, 48×3, 60, 72) — **12 fonts** | ✅ exact | ✅ exact | ✅ exact | ±1 avg | ±1-3 some |
| **CJK divergence** | Batang, BatangChe — **2 fonts** | -4/-5 | -2/-3 | up to 23px | up to 13 | up to 9 |
| **Minor divergence** | Bell MT (16pt, 48pt) — **2 configs** | -1 (16pt only) | ✅ (48pt) | up to 6 | up to 12 | up to 12 |

### Remaining Issues

1. **xoffset ±1 systematic** on most fonts (padding is 0 in all test configs, so not padding-related)
2. **Batang/BatangChe major divergence** — different font sizing or font selection path
3. **Bell MT xadvance/kerning differences** — kerning amount diffs on 218 shared pairs
4. **Atlas PNG channel issue** — KernSmith produces white-on-black, BMFont produces white-on-alpha (separate bug, not metrics)

## BMFont Source Code Analysis

Read from BMFont source (GitHub mirrors: kylawl/bmfont, xrModder/BMFont; SourceForge SVN). Key files: `fontchar.cpp`, `fontgen.cpp`, `unicode.cpp`, `fontpage.cpp`.

**NOTE**: A comprehensive REF-08 document analyzing the BMFont source is being created separately. The findings below are preliminary and should be validated against REF-08.

### How BMFont works

**Font creation**: BMFont uses `::CreateFont(fontSize*aa, 0, 0, 0, weight, italic, 0, 0, charSet, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY, DEFAULT_PITCH|FF_DONTCARE, fontName)`. It does NOT load font bytes — it uses font name selection directly through GDI.

**Font sizing**: The `fontSize` from .bmfc is passed directly to `CreateFont` as the `cHeight` parameter. Negative fontSize in .bmfc = negative cHeight = character height mode (em height). Positive fontSize = cell height mode. No DPI scaling. The value is multiplied by `aa` (supersampling factor, default 1).

**lineHeight/base**: `GetTextMetrics(dc, &tm)` then `lineHeight = ceil(tmHeight / aa)`, `base = ceil(tmAscent / aa)` (at scaleH=100). GetTextMetrics is called BEFORE world transform is applied.

**Padding**: Applied in `CFontPage::AddChar` AFTER glyph rendering:
```cpp
ch->m_xoffset -= paddingLeft;
ch->m_yoffset -= paddingUp;
ch->m_width += paddingLeft + paddingRight;
ch->m_height += paddingUp + paddingDown;
```

**Glyph rendering**: Two paths:
- Outline path (`DrawGlyphFromOutline`): uses `GetGlyphOutline` with supersampled transforms. `yoffset = fontAscent - (maxY / 65536)`, where fontAscent = `ceil(tmAscent * scaleH / 100)`.
- Bitmap path (`DrawGlyphFromBitmap`): renders to a bitmap, trims empty rows/columns, adjusts xoffset/yoffset accordingly. `xoffset` starts as `abc.abcA` (from `GetCharABCWidths`).

**Kerning**: 3-tier approach:
1. `GetKerningPairsW(dc, ...)` — reads legacy kern table
2. If 0 pairs, manual GPOS parsing via `GetFontData(dc, 'GPOS', ...)` — PairAdjustment Format 1/2 + Extension Type 9
3. KERN table parsing via `GetFontData`
Amounts divided by `aa` when writing.

**Outline effects**: `xoffset -= thickness`, `yoffset -= thickness`, `width += 2*thickness`, `height += 2*thickness`.

**Channel output**: BMFont writes glyphs with configurable channel mapping (`alphaChnl`, `redChnl`, `greenChnl`, `blueChnl`). Common config: alpha=glyph data (0), RGB=constant white (4).

## Root Cause Analysis

### Root Cause 1: Font loading approach differs fundamentally — HIGHEST IMPACT (Batang)

**BMFont**: Calls `CreateFont("Batang", ...)` directly. GDI resolves the font name to the system-installed font (batang.ttc) using its font mapper, which handles TTC face selection, font substitution, and composite font mapping internally.

**KernSmith GDI**: Loads font file bytes, calls `AddFontMemResourceEx` to register privately, parses the family name from the TTF name table, then calls `CreateFontIndirectW` with that parsed name. This is a fundamentally different font resolution path.

For composite CJK fonts like Batang (shipped as batang.ttc containing Batang, BatangChe, Gungsuh, GungsuhChe), the `AddFontMemResourceEx` + name parsing approach may:
- Select a different face from the collection
- Register all faces but parse the wrong name
- Produce different TEXTMETRIC values due to different font mapper behavior for privately-registered vs system-installed fonts

**Evidence**: Tested `OUT_DEFAULT_PRECIS`, `OUT_TT_PRECIS`, and `OUT_TT_ONLY_PRECIS` — all three produce identical tmHeight=32 for KernSmith, while BMFont gets tmHeight=36. This confirms the issue is not `lfOutPrecision` but the font resolution path itself.

**Potential fix**: For system fonts, add a code path that creates HFONT directly by name (matching BMFont) instead of loading bytes + registering. The `IRasterizer.LoadFont(ReadOnlyMemory<byte>)` interface would still work for embedded/custom fonts, but system fonts could bypass the byte-loading path.

### Root Cause 2: lineHeight/base from OS/2 tables instead of GDI TEXTMETRIC

**Status: FIXED.** `GetFontMetrics()` now uses `GetTextMetricsW`. Exact match on 12/15 fonts.

### Root Cause 3: Padding not subtracted from xoffset/yoffset — CONFIRMED

BMFont subtracts padding from offsets and adds to dimensions. **Status: FIXED in code**, but all test configs have padding=0, so cannot validate. Fix is correct per BMFont source.

### Root Cause 4: xoffset ±1 on non-padded fonts — UNKNOWN

Systematic xoffset +1 persists even with padding=0. Possible causes:
- **Glyph origin rounding**: `GetGlyphOutlineW` GLYPHMETRICS.gmptGlyphOrigin may round differently depending on the rendering path or precision flags
- **BMFont's bitmap path**: BMFont may use the bitmap rendering path (DrawGlyphFromBitmap) for some fonts, which derives xoffset from `GetCharABCWidths.abcA` + trimming, not from `GetGlyphOutline` bearings
- **BMFont's outline path**: Uses supersampled transforms and computes `xoffset = int(minX) / scale` from the glyph outline bounding box, which may differ from GLYPHMETRICS.gmptGlyphOrigin.x

Needs investigation: determine which rendering path BMFont uses for each test font, and whether KernSmith's `GetGlyphOutlineW` approach produces different glyph origins.

### Root Cause 5: Kerning gaps

**GetKerningPairsW returns 0 for GPOS-only fonts**: **Status: FIXED.** Returns null to fall back to GPOS parser.

**Remaining kerning issues**:
- Bahnschrift: BMFont has 47-63 extra pairs beyond what KernSmith's GPOS parser finds. BMFont's GPOS parser may handle class-based pairs (Format 2) more expansively.
- Bell MT: 218 shared pairs have different amounts. May be a scaling difference or a kern-vs-GPOS source difference.
- Batang: BMFont has 88-91 pairs, KernSmith has 0. May be related to the font resolution path difference (root cause #1) — GDI `GetKerningPairsW` on the system-installed font may return pairs that the privately-registered font doesn't.

### Root Cause 6: Cell-height-to-ppem round-trip

**Status: FIXED.** `HandlesOwnSizing` flag skips the conversion.

### Root Cause 7: Atlas PNG channel configuration

KernSmith ignores `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` from .bmfc configs. Produces white-on-black instead of BMFont's white-on-alpha. **Separate bug, not metrics-related.** Tracked in memory: `project_atlas_channel_bug.md`.

## What Was Implemented

### First Pass (Complete)

| Item | File(s) | Status |
|------|---------|--------|
| `HandlesOwnSizing` capability flag | IRasterizerCapabilities.cs | ✅ Done |
| `GetFontMetrics()` default method | IRasterizer.cs, RasterizerFontMetrics.cs | ✅ Done |
| `GetKerningPairs()` default method | IRasterizer.cs, ScaledKerningPair.cs | ✅ Done |
| GDI implements GetFontMetrics (GetTextMetricsW) | GdiRasterizer.cs | ✅ Done |
| GDI implements GetKerningPairs (GetKerningPairsW) | GdiRasterizer.cs, NativeMethods.cs | ✅ Done |
| BmFont.cs skips cell-height scaling when HandlesOwnSizing | BmFont.cs | ✅ Done |
| BmFontModelBuilder uses rasterizer metrics/kerning | BmFontModelBuilder.cs | ✅ Done |
| Captured values before rasterizer disposal | RasterizationResult.cs, BmFont.cs | ✅ Done |
| FreeTypeRasterizer: HandlesOwnSizing = false | FreeTypeRasterizer.cs | ✅ Done |

### Second Pass (Complete)

| Item | File(s) | Status |
|------|---------|--------|
| Padding subtracted from xoffset/yoffset | BmFontModelBuilder.cs | ✅ Done (untestable — configs have padding=0) |
| Kerning fallback: return null when GDI returns 0 pairs | GdiRasterizer.cs | ✅ Done |
| TTC (font collection) support in ParseFamilyName | GdiRasterizer.cs | ✅ Done |

### Attempted but Reverted

| Item | Result |
|------|--------|
| lfHeight sign flip (positive for cell height) | ❌ Made ALL fonts worse. BMFont uses negative fontSize from .bmfc directly, not positive. Reverted. |
| lfOutPrecision changes (DEFAULT, TT, TT_ONLY) | ❌ No effect on Batang metrics. Issue is font resolution path, not precision flags. |

## Remaining Work

### Blocked on REF-08

The following items need the comprehensive BMFont source analysis (REF-08) before proceeding:

1. **Batang/BatangChe font resolution** — need to understand exactly how BMFont resolves system fonts vs KernSmith's byte-loading approach. May need a `LoadSystemFont(string familyName)` path on `IRasterizer`.

2. **xoffset ±1 systematic offset** — need to determine which rendering path BMFont uses (outline vs bitmap) and how it computes glyph origins. KernSmith uses `GetGlyphOutlineW` GLYPHMETRICS exclusively.

3. **Bell MT kerning amount differences** — need to understand BMFont's kerning scaling and whether Bell MT uses kern or GPOS tables.

4. **BMFont's glyph rendering pipeline details** — supersampling, world transforms, how `DrawGlyphFromOutline` computes metrics vs `DrawGlyphFromBitmap`.

### Independent of REF-08

5. **Atlas PNG channel configuration** — separate bug, can be fixed independently. KernSmith ignores alphaChnl/redChnl/greenChnl/blueChnl.

## Files Created/Changed (All Passes)

| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs` | Add `HandlesOwnSizing => false` default method |
| `src/KernSmith/Rasterizer/IRasterizer.cs` | Add `GetFontMetrics()` and `GetKerningPairs()` default methods |
| `src/KernSmith/Rasterizer/RasterizerFontMetrics.cs` | New — font metrics record type |
| `src/KernSmith/Font/Models/ScaledKerningPair.cs` | New — pixel-scaled kerning pair type |
| `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` | Add `HandlesOwnSizing = false` to capabilities |
| `src/KernSmith/RasterizationResult.cs` | Add captured rasterizer metrics/kerning properties |
| `src/KernSmith/BmFont.cs` | Skip cell-height scaling when HandlesOwnSizing; capture metrics before disposal |
| `src/KernSmith/Output/BmFontModelBuilder.cs` | Use rasterizer metrics/kerning; apply padding to offsets/dimensions |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | Implement GetFontMetrics/GetKerningPairs, TTC parsing, kerning null fallback |
| `src/KernSmith.Rasterizers.Gdi/NativeMethods.cs` | Add GetKerningPairsW P/Invoke, KERNINGPAIR struct, OUT_DEFAULT_PRECIS/OUT_TT_ONLY_PRECIS constants |

## Testing

- ✅ All 330 FreeType tests pass (zero behavioral change)
- ✅ All 14 GDI tests pass (344 total on Windows TFMs)
- ✅ 12/15 fonts: lineHeight/base/xadvance exact match
- ⚠️ 2 fonts (Batang/BatangChe): significant divergence — blocked on font resolution investigation
- ⚠️ 1 font config (Bell MT 16pt): minor divergence — kerning and lineHeight -1
- ⚠️ xoffset ±1 systematic — needs rendering path investigation
