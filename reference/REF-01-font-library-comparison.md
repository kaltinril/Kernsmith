# C# Font Parsing & Rasterization Library Comparison

> Research date: 2026-03-18
> Purpose: Evaluate open-source C# libraries for TTF/OTF parsing, glyph metric extraction, kerning, and bitmap rasterization for a cross-platform NuGet package (game-runtime use case).

---

## Executive Summary

| Library | License | Pure Managed | Parse TTF/OTF | Metrics/Kerning | Rasterize | System Fonts | Verdict |
|---------|---------|:---:|:---:|:---:|:---:|:---:|---------|
| **SixLabors.Fonts** | Six Labors Split License | Yes | Yes | Yes | No* | Yes | **DISQUALIFIED** — restrictive license |
| **SkiaSharp** | MIT | No (native Skia) | Yes | Yes | Yes | Yes | **Viable** — but heavy (~130MB native) |
| **FreeTypeSharp** | MIT | No (native FreeType) | Yes | Yes | Yes | No | **Viable** — lighter native dep |
| **SharpFont** | MIT | No (native FreeType) | Yes | Yes | Yes | No | **Dead** — last update 2016 |
| **Typography.OpenFont** | MIT | Yes | Yes | Yes | No | No | **Stale** — NuGet unlisted, last update 2018 |
| **Custom Parser** | N/A | Yes | Feasible | Feasible | Hard | Manual | **Feasible** for parsing; rasterization is complex |
| **Hybrid Approach** | Depends | Partial | Yes | Yes | Yes | Yes | **RECOMMENDED** |

\* SixLabors.Fonts can shape/measure text but cannot rasterize glyphs to bitmaps on its own. Rasterization requires SixLabors.ImageSharp.Drawing, which shares the same restrictive license.

---

## 1. SixLabors.Fonts

**Repository:** https://github.com/SixLabors/Fonts
**NuGet:** SixLabors.Fonts — 188.3M downloads, latest v2.1.3 (April 2025)
**License:** Six Labors Split License v1.0

### License Analysis — DISQUALIFYING

The Six Labors Split License is a **custom dual license** (NOT OSI-approved):

- **Apache 2.0 applies IF:**
  - Open source / source available project
  - Transitive package dependency (not direct)
  - For-profit entity with **< $1M USD annual gross revenue**
  - Non-profit / registered charity
- **Commercial license required for all other scenarios** — must purchase at sixlabors.com/pricing

**This is NOT a true open-source license.** The revenue gate and commercial license requirement make it a split/freemium license. For a NuGet package that others will consume, this creates license compliance headaches for downstream users.

**SixLabors.ImageSharp** (needed for rasterization via ImageSharp.Drawing) uses the **same Split License**, compounding the issue.

### Capabilities
- Parse: TTF, OTF, CFF1/CFF2, WOFF/WOFF2
- GSUB/GPOS (advanced OpenType layout)
- Text shaping, BiDi, line breaking
- Variable fonts, color fonts
- **Cannot rasterize glyphs** — measurement and layout only
- System font enumeration: Yes

### Dependencies
- Pure managed C# — no native dependencies
- .NET 6.0+

### Verdict
**DISQUALIFIED.** The Split License is incompatible with a project that cannot use restrictively-licensed dependencies. Even if you qualify for Apache 2.0 today, downstream consumers of your package may not.

---

## 2. SkiaSharp

**Repository:** https://github.com/mono/SkiaSharp — 5.3k stars, 143 releases, actively maintained
**NuGet:** SkiaSharp — 243.6M downloads, latest v3.119.2 (Feb 2026)
**License:** MIT (OSI-approved) ✓

### License Analysis
Pure MIT license. No restrictions. Safe for any use.

### Capabilities
- Full 2D graphics API based on Google Skia
- Parse TTF/OTF fonts via `SKTypeface.FromFile()` / `SKTypeface.FromStream()`
- Extract glyph metrics, advances, bounds
- Rasterize glyphs to bitmaps via `SKCanvas` + `SKPaint`
- HarfBuzzSharp available for advanced text shaping (MIT, 67.7M downloads)
- System font enumeration via `SKFontManager`
- Kerning via HarfBuzz or Skia's built-in shaper

### Native Dependencies — THE MAJOR CONCERN
SkiaSharp wraps Google's Skia C++ library via platform-specific native packages:

| Native Package | Size |
|---|---|
| SkiaSharp.NativeAssets.Win32 | **73.25 MB** |
| SkiaSharp.NativeAssets.Linux | **52.64 MB** |
| SkiaSharp.NativeAssets.macOS | **6.77 MB** |
| SkiaSharp (managed) | 4.47 MB |

**Total native footprint: ~130+ MB across platforms.** This is extremely heavy for a font-only use case — you're shipping an entire 2D graphics engine just to rasterize glyphs.

### Cross-Platform Support
Excellent: Windows, macOS, Linux, Android, iOS, tvOS, Tizen, WASM, Mac Catalyst

### Performance
Skia is battle-tested in Chrome and Android. Font rasterization is fast with hardware-quality anti-aliasing. However, the library initializes a full graphics pipeline which may be overkill.

### Verdict
**Viable but overkill.** MIT license is perfect. Capabilities are complete. But the ~130MB native dependency footprint is excessive for a package that only needs font parsing and glyph rasterization. Best used if the consuming application already depends on SkiaSharp for other rendering.

---

## 3. FreeTypeSharp

**Repository:** https://github.com/ryancheung/FreeTypeSharp — 83 stars, actively maintained
**NuGet:** FreeTypeSharp — 84.2K downloads, latest v3.1.0 (Feb 2026)
**License:** MIT (OSI-approved) ✓

### License Analysis
MIT license. FreeType itself is dual-licensed under FreeType License (BSD-like) or GPLv2 — the FreeType License is permissive and compatible.

### Capabilities
- Low-level P/Invoke bindings to FreeType 2.13.2
- Load TTF/OTF/TTC/WOFF fonts
- Extract glyph metrics (advances, bearings, bounding boxes)
- Rasterize glyphs to monochrome or anti-aliased bitmaps
- Hinting support
- Color emoji support (v3.1.0)
- Kerning via `FT_Get_Kerning`
- **Does NOT enumerate system fonts** — you must provide font file paths

### Native Dependencies
- Bundles pre-compiled FreeType native binaries for all platforms via CI
- Native binaries included in the NuGet package (much smaller than Skia)
- Platforms: Windows, Linux, macOS, Android, iOS, tvOS

### API Style
Almost identical to the C FreeType API — low-level, requires manual resource management. No high-level abstractions. You work with `FT_Library`, `FT_Face`, `FT_GlyphSlot`, etc.

### Maintenance Status
Active — latest release Feb 2026, wraps FreeType 2.13.2, 149 commits, 2 contributors. Small but focused project.

### Verdict
**Strong candidate for rasterization.** MIT license, small native footprint, battle-tested FreeType underneath, actively maintained. The low-level API means more work to integrate, but it does exactly what we need for rasterization. Lacks system font enumeration.

---

## 4. SharpFont

**Repository:** https://github.com/Robmaister/SharpFont — "Need maintainer" banner
**NuGet:** SharpFont — 320.8K downloads, latest v4.0.1 (August 2016)
**License:** MIT (OSI-approved) ✓

### Status: DEAD

- Last NuGet update: **August 2016** (nearly 10 years ago)
- Targets .NET Framework 4.5 only
- Repository explicitly states "Need maintainer"
- Depends on native FreeType (`freetype6.dll`)
- No .NET Standard / .NET Core / .NET 6+ support

### Verdict
**DISQUALIFIED.** Unmaintained for 10 years. No modern .NET support. FreeTypeSharp is the active successor in this space.

---

## 5. Typography / Typography.OpenFont (LayoutFarm)

**Repository:** https://github.com/LayoutFarm/Typography — 3,247 commits
**NuGet:** Typography.OpenFont — 2.3K downloads, v1.0.0 (Jan 2018, **UNLISTED**)
**License:** MIT (with components under Apache 2.0 and BSD)

### What Is It?
A pure C# font reader and glyph layout engine. "Pure C# Font Reader, Glyph Layout and Rendering."

### Capabilities
- Parse TTF, OTF, TTC, OTC, WOFF, WOFF2
- OpenType layout (GSUB/GPOS)
- Glyph outline extraction
- Text shaping
- **Does NOT include glyph rasterization** — the core modules "do NOT provide a glyph rendering implementation"
- No system font enumeration

### Status
- NuGet package is **unlisted** (effectively deprecated on NuGet)
- GitHub repo has many commits but unclear maintenance cadence
- Not widely adopted (2.3K downloads)

### Verdict
**Not recommended.** While the pure-managed C# approach is attractive and the MIT license is clean, the NuGet package is unlisted, the project has minimal adoption, and it doesn't include rasterization. The codebase could be studied for reference if building a custom parser, but it's not a reliable dependency.

---

## 6. Custom Minimal TTF Parser

### What Would It Take?

**Parsing (Medium difficulty):**
The TTF/OTF format is well-documented. Required tables:

| Table | Purpose | Complexity |
|-------|---------|-----------|
| `head` | Font header, units-per-em | Simple |
| `maxp` | Maximum profile (glyph count) | Simple |
| `name` | Font name strings | Simple |
| `OS/2` | Metrics, weight, width class | Simple |
| `cmap` | Character-to-glyph mapping | Medium (multiple formats) |
| `hhea` | Horizontal header | Simple |
| `hmtx` | Horizontal metrics per glyph | Simple |
| `loca` | Glyph data offsets | Simple (two formats) |
| `glyf` | Glyph outlines (TrueType) | Medium (composite glyphs) |
| `kern` | Legacy kerning | Medium |
| `GPOS` | Advanced positioning/kerning | Hard (complex structure) |

Parsing TTF tables is feasible in pure C# — it's binary data with documented structures. Estimate: 2,000-4,000 lines of code for a solid parser covering the tables above.

**Rasterization (Hard):**
This is the difficult part. Converting glyph outlines to bitmaps requires:

1. **Outline processing:** TrueType uses quadratic Bezier curves; CFF/OTF uses cubic Beziers. Must handle composite glyphs (glyphs built from other glyphs).

2. **Scan conversion:** Converting vector outlines to a pixel grid. The standard approach:
   - Flatten curves to line segments
   - Use a scanline fill algorithm (even-odd or non-zero winding)
   - For each scanline, find edge crossings, sort, fill between pairs

3. **Anti-aliasing:** The quality bar. Options:
   - **No AA:** Simplest — binary in/out. Looks terrible at small sizes.
   - **Supersampling:** Render at Nx resolution, downsample. Simple but slow (N^2 cost).
   - **Analytic AA:** Compute exact pixel coverage from edge geometry. Best quality/performance ratio but significantly harder to implement. This is what FreeType and Skia use.
   - **Signed Distance Fields (SDF):** Common in games. Render once, scale freely via shader. Could be a great fit but still needs initial rasterization.

4. **Hinting (optional but important):** TrueType hinting involves a bytecode VM. CFF hinting is simpler but still complex. Without hinting, small sizes look poor on low-DPI displays. For bitmap font generation (the bmfontier use case), hinting at the target size matters.

**Effort estimate for rasterization:**
- Basic scanline rasterizer with supersampled AA: 1,000-2,000 lines, 1-2 weeks
- Production-quality analytic AA: 3,000-5,000 lines, 4-8 weeks
- With hinting: Add 3,000+ lines and weeks of debugging

### Verdict
**Parsing is feasible and worthwhile; rasterization is a rabbit hole.** A custom TTF parser gives full control and zero dependencies. But implementing a quality rasterizer is a significant undertaking — FreeType has had decades of refinement. Better to delegate rasterization to a proven native library.

---

## 7. System Font Enumeration

### Per-Platform Approaches

| Platform | Standard Font Directories | API Approach |
|----------|--------------------------|-------------|
| **Windows** | `C:\Windows\Fonts`, `%LOCALAPPDATA%\Microsoft\Windows\Fonts` | Registry: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts` |
| **macOS** | `/System/Library/Fonts`, `/Library/Fonts`, `~/Library/Fonts` | Directory scan or CoreText via P/Invoke |
| **Linux** | `/usr/share/fonts`, `/usr/local/share/fonts`, `~/.fonts`, `~/.local/share/fonts` | `fc-list` (fontconfig) or directory scan |

### Library Support

| Library | System Font Enumeration |
|---------|------------------------|
| SixLabors.Fonts | Yes — `SystemFonts` class |
| SkiaSharp | Yes — `SKFontManager.Default` |
| FreeTypeSharp | No — must provide file paths |
| SharpFont | No |
| Typography.OpenFont | No |

### Lightweight Cross-Platform Approach
A custom implementation is straightforward:
1. Detect OS via `RuntimeInformation.IsOSPlatform()`
2. Scan known font directories for `.ttf`, `.otf`, `.ttc` files
3. On Windows, optionally read the registry for the canonical font list
4. Parse the `name` table from each font file to get family/style names
5. Cache results

This is ~200-400 lines of code and has zero dependencies. Most game/tool applications need this anyway since they want to present a font picker.

---

## 8. Hybrid Approach — RECOMMENDED

### Architecture

```
┌─────────────────────────────────────────────┐
│              bmfontier                       │
├─────────────────────────────────────────────┤
│  Custom Pure-C# TTF/OTF Parser              │
│  ├── Table reading (cmap, glyf, head, etc.) │
│  ├── Glyph metric extraction                │
│  ├── Kerning (kern table + basic GPOS)      │
│  ├── System font enumeration                │
│  └── Font metadata / name table             │
├─────────────────────────────────────────────┤
│  Rasterization Backend (pluggable)          │
│  ├── FreeTypeSharp (default, recommended)   │
│  │   └── MIT + FreeType License             │
│  │   └── Small native dep (~5-10MB)         │
│  └── SkiaSharp (optional, if already used)  │
│       └── MIT                               │
│       └── Large native dep (~130MB)         │
└─────────────────────────────────────────────┘
```

### Why This Approach?

1. **Custom parser for metadata/metrics:**
   - Zero dependencies for reading font info, metrics, kerning
   - Full control over what tables to parse and how
   - Pure managed C# — no native deps for the common case of "just reading font data"
   - ~2,000-4,000 lines, well-scoped work

2. **FreeTypeSharp for rasterization:**
   - MIT license — no restrictions
   - FreeType is the gold standard for font rasterization
   - Battle-tested hinting, anti-aliasing, subpixel rendering
   - Native binaries bundled in NuGet (~5-10MB per platform, much lighter than Skia)
   - Actively maintained, supports all target platforms

3. **Optional SkiaSharp backend:**
   - For users who already have SkiaSharp in their project
   - Avoids adding FreeType when Skia is already present
   - Could be a separate NuGet package (e.g., `bmfontier.SkiaSharp`)

### License Cleanliness
- Custom parser: Your own code, your license
- FreeTypeSharp: MIT ✓
- FreeType native: FreeType License (BSD-like, permissive) ✓
- SkiaSharp: MIT ✓
- **No paid, split, or restrictive licenses anywhere in the chain**

---

## Capabilities Matrix

| Capability | SixLabors.Fonts | SkiaSharp | FreeTypeSharp | Custom Parser | Hybrid |
|---|:---:|:---:|:---:|:---:|:---:|
| Parse TTF | ✓ | ✓ | ✓ | ✓ | ✓ |
| Parse OTF (CFF) | ✓ | ✓ | ✓ | Partial* | ✓ |
| Parse WOFF/WOFF2 | ✓ | ✗ | ✓ | ✗ | ✓ (via FreeType) |
| Glyph metrics | ✓ | ✓ | ✓ | ✓ | ✓ |
| Kerning (kern) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Kerning (GPOS) | ✓ | ✓ | ✓ | Hard | ✓ (via FreeType) |
| Rasterize glyphs | ✗ | ✓ | ✓ | Hard | ✓ |
| Anti-aliased rendering | ✗ | ✓ | ✓ | Very Hard | ✓ |
| Hinting | N/A | ✓ | ✓ | Very Hard | ✓ |
| SDF generation | ✗ | ✗ | ✗** | Hard | Custom** |
| System font enum | ✓ | ✓ | ✗ | ✓ | ✓ |
| Color emoji | ✓ | ✓ | ✓ | ✗ | ✓ |
| Variable fonts | ✓ | ✓ | ✓ | ✗ | ✓ (via FreeType) |
| Pure managed C# | ✓ | ✗ | ✗ | ✓ | Partial |

\* CFF outline parsing is more complex than TrueType `glyf` — would need a CFF interpreter.
\** FreeType 2.11+ has built-in SDF rendering mode (`FT_RENDER_MODE_SDF`).

---

## NuGet Package Sizes (Approximate)

| Package | Managed Size | Native Size (per platform) | Total (3 platforms) |
|---------|---|---|---|
| SkiaSharp + natives | 4.5 MB | 44-73 MB | ~130+ MB |
| FreeTypeSharp | < 1 MB | ~5-10 MB | ~15-30 MB |
| SixLabors.Fonts | < 1 MB | 0 (managed) | < 1 MB |
| Custom parser | 0 (your code) | 0 | 0 |

---

## Recommendation

### Primary: Hybrid with FreeTypeSharp for Rasterization

**For bmfontier, the recommended approach is:**

1. **Write a custom pure-C# TTF/OTF parser** for:
   - Font file discovery / system font enumeration
   - Reading font metadata (name table, OS/2 metrics)
   - Extracting per-glyph horizontal metrics (hmtx)
   - Reading the kern table for pair kerning
   - Character-to-glyph mapping (cmap)
   - This gives you zero-dependency font inspection and metric extraction

2. **Use FreeTypeSharp for rasterization** because:
   - MIT license — fully permissive, no revenue gates
   - FreeType is the industry standard rasterizer (used in Linux, Android, Chrome, etc.)
   - Excellent anti-aliasing and hinting
   - FreeType 2.11+ supports SDF rendering mode — perfect for games
   - Native dependency is small (~5-10MB per platform vs ~130MB for Skia)
   - Actively maintained, cross-platform native binaries bundled in NuGet

3. **Do NOT use SixLabors.Fonts** — the Split License creates downstream compliance risk regardless of your current revenue.

4. **Consider SkiaSharp only as an optional alternative backend** for projects that already depend on it.

### Why Not Just FreeTypeSharp for Everything?

You could use FreeTypeSharp for parsing too (it can do everything FreeType can). But having a custom managed parser gives you:
- Ability to inspect fonts without loading native code
- Faster startup for metadata-only operations
- No native dependency for the "enumerate and pick fonts" workflow
- Full control over how you interpret font tables
- Easier unit testing

### Risk Mitigation
- FreeTypeSharp has only 83 stars / 2 contributors — if it becomes unmaintained, the P/Invoke layer is thin enough to maintain yourself or fork
- FreeType itself is extremely stable and well-maintained (decades of development)
- The custom parser is your own code with no external risk
