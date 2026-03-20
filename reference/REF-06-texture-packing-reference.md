# Texture Atlas Packing Algorithms for BMFont Generation

## Table of Contents

1. [Problem Definition](#problem-definition)
2. [Algorithm Survey](#algorithm-survey)
   - [MaxRects](#1-maxrects-maximum-rectangles)
   - [Shelf Packing](#2-shelf-packing)
   - [Skyline Packing](#3-skyline-packing)
   - [Guillotine Packing](#4-guillotine-packing)
3. [Algorithm Comparison](#algorithm-comparison)
4. [BMFont Format Constraints](#bmfont-format-constraints)
5. [What Existing Tools Use](#what-existing-tools-use)
6. [Multi-Page Strategy](#multi-page-strategy)
7. [Existing C# Implementations](#existing-c-implementations)
8. [Recommendation for bmfontier](#recommendation-for-bmfontier)
9. [References](#references)

---

## Problem Definition

Given a set of rectangular glyph bitmaps (each with width, height, and configurable padding), pack them into one or more fixed-size texture pages (preferably power-of-2 dimensions) to minimize wasted space and page count. Constraints:

- **No rotation** (BMFont format has no rotation field)
- **Axis-aligned rectangles only** (integer pixel coordinates)
- **Configurable padding/spacing** between glyphs (added to each glyph's effective size before packing)
- **Power-of-2 texture sizes** recommended (256, 512, 1024, 2048)
- **Multiple texture pages** supported when glyphs overflow a single page

This is a variant of the 2D bin packing problem, which is NP-hard in the general case. All practical solutions use heuristic algorithms.

---

## Algorithm Survey

### 1. MaxRects (Maximum Rectangles)

**The most widely recommended algorithm for texture atlas packing.**

Source: Jukka Jylanki, "A Thousand Ways to Pack the Bin" (2010), with reference implementation at github.com/juj/RectangleBinPack.

#### How It Works

MaxRects maintains a list of **free rectangles** representing all available empty space in the bin. When a new rectangle is placed, any free rectangle that overlaps the placed rectangle is split into up to four new free rectangles (top, bottom, left, right residuals). Redundant free rectangles (those fully contained within another) are pruned.

```
Initial state:           After placing A (3x2):     Free rectangles:
+----------------+       +---+-------------+       F1: right of A
|                |       | A |     F1      |       F2: below A
|                |       |   |             |       (overlapping regions
|                |       +---+------+------+        are tracked and
|                |       |   F2    |  F1   |        pruned later)
|                |       |         |       |
+----------------+       +---------+-------+

After placing B (2x3):   Free rectangles updated:
+---+--+----------+      Multiple overlapping free
| A |B |          |      rects tracked, pruned to
|   |  |   F1'    |      remove those contained
+---+  +----------+      within others.
|   |  |          |
|   +--+   F2'    |
|      |          |
+------+----------+
```

#### Key Mechanism: Free Rectangle Splitting

When rectangle R is placed overlapping free rectangle F:
1. If F extends **right** of R: create new free rect for right portion
2. If F extends **left** of R: create new free rect for left portion
3. If F extends **below** R: create new free rect for bottom portion
4. If F extends **above** R: create new free rect for top portion

After splitting, **prune** the free list by removing any rectangle fully contained within another. This is O(n^2) per insertion but keeps the free list minimal.

#### Heuristic Variants

Each variant scores candidate placements differently:

| Variant | Strategy | Best For |
|---------|----------|----------|
| **BestShortSideFit (BSSF)** | Minimize leftover on shorter side of fit | General purpose, good default |
| **BestLongSideFit (BLSF)** | Minimize leftover on longer side of fit | When rectangles are similar width |
| **BestAreaFit (BAF)** | Choose smallest free rect by area; short-side tiebreaker | Preserving large free spaces |
| **BottomLeftRule (BL)** | Place as low as possible, then as far left | Tetris-style, predictable layout |
| **ContactPointRule (CP)** | Maximize edge contact with placed rects and bin walls | Tight clustering, good visual result |

**Batch mode**: When placing multiple rectangles, evaluate ALL remaining rectangles against ALL free rects, pick the globally best placement, repeat. This produces significantly better results than inserting in a fixed order.

#### Performance Characteristics

- **Time complexity**: O(n^2) per insertion due to free list pruning; O(n^3) total for batch mode with n rectangles
- **Space complexity**: O(n) free rectangles (typically 2-3x the number of placed rectangles)
- **Packing efficiency**: 93-97% for typical glyph sets (hundreds of similarly-sized small rectangles)
- **Practical speed**: Fast enough for font generation (milliseconds for hundreds of glyphs)

---

### 2. Shelf Packing

#### How It Works

Divides the bin into horizontal **shelves** (rows). Each shelf has a fixed height (set by the tallest rectangle placed on it). Rectangles are placed left-to-right within a shelf.

```
+--+---+----+--+--------+
|A |B  | C  |D | waste  |   Shelf 1 (height = max(A,B,C,D))
+--+---+----+--+--------+
|E   |F  |G | waste    |   Shelf 2 (height = max(E,F,G))
+-----+---+--+----------+
|H |I    | waste        |   Shelf 3 (height = max(H,I))
+--+------+--------------+
|     remaining space     |
+-------------------------+
```

#### Shelf Heuristics

| Heuristic | Description |
|-----------|-------------|
| **Next Fit (NF)** | Only try the current (most recent) shelf |
| **First Fit (FF)** | Try shelves in order, use the first that fits |
| **Best Height Fit (BHF)** | Choose shelf whose height best matches the rectangle |
| **Best Area Fit (BAF)** | Choose shelf with smallest remaining area |
| **Best Width Fit (BWF)** | Choose shelf with least remaining horizontal space |
| **Worst Width Fit (WWF)** | Choose shelf with most remaining horizontal space |

#### Characteristics

- **Simplicity**: Very easy to implement (50-100 lines of code)
- **Speed**: O(n * shelves) per insertion, very fast
- **Efficiency**: 80-90% typical; wastes space above short rectangles on tall shelves
- **Best for**: Glyphs of similar height (which is common for single-size fonts)
- **Weakness**: Poor when glyph heights vary significantly (e.g., 'a' vs 'j' vs combining diacritics)

---

### 3. Skyline Packing

#### How It Works

Maintains a **skyline** -- a 1D height map representing the top edge of occupied space. The skyline is a list of (x, y, width) segments. When placing a rectangle, find the position along the skyline that minimizes the resulting height.

```
Skyline state:                 After placing D:
        +--+                          +--+
     +--+D |                       +--+D |
  +--+C |  |                    +--+C |  |
+-+B |  |  |                  +-+B |  |  |
|A|  |  |  |    skyline -->   |A|  |  |  +--+
|A|  |  |  |    =========    |A|  |  |  |E |
+--+--+--+--+-------+        +--+--+--+--+--+--+
 ^  ^  ^  ^  ^                 Skyline segments:
 Skyline segments:             (0,2,1)(1,3,1)(2,4,1)
 (0,2,1)(1,3,1)(2,4,1)        (3,5,1)(4,2,1)(5,1,1)
 (3,5,1)(4,0,rest)
```

#### Skyline Heuristics

| Heuristic | Description |
|-----------|-------------|
| **Bottom-Left** | Minimize y-coordinate of placed rectangle; break ties by x |
| **Min Waste Fit** | Minimize wasted area between rectangle bottom and skyline beneath it |

**Waste map optimization**: Some implementations (including Jylanki's) use a secondary allocator (e.g., Guillotine) to track and reuse the wasted space between the skyline and placed rectangles.

#### Characteristics

- **Time complexity**: O(n * skyline_segments) per insertion, typically O(n^2) total
- **Space complexity**: O(width) skyline segments
- **Efficiency**: 90-95% typical; very competitive with MaxRects
- **Speed**: Faster than MaxRects (simpler data structure)
- **Notable users**: stb_rect_pack (Sean Barrett), fontstash, nanovg, Dear ImGui, Firefox WebRender (via `etagere` crate)
- **Key advantage**: Simple, fast, good packing -- an excellent middle ground

---

### 4. Guillotine Packing

#### How It Works

Similar to MaxRects but with a key constraint: when a rectangle is placed in a free rectangle, the remaining L-shaped space is split by a single **guillotine cut** (either horizontal or vertical) into exactly **two** new free rectangles, not up to four.

```
Free rect F:          Place R, horizontal split:    Place R, vertical split:
+----------+         +---+-------+                 +---+--+----+
|          |         | R |       |                 | R |       |
|          |   -->   +---+  F2   |            OR   |   |  F2   |
|          |         |   |       |                 +---+       |
|    F     |         |F1 +-------+                 |F1 |       |
|          |         |   |                         |   +-------+
+----------+         +---+                         +---+-------+
```

#### Split Strategies

| Strategy | Rule |
|----------|------|
| **SplitShorterLeftoverAxis** | Cut along the axis with smaller remaining space |
| **SplitLongerLeftoverAxis** | Cut along the axis with larger remaining space |
| **SplitMinimizeArea** | Try to make the single bigger rectangle |
| **SplitMaximizeArea** | Try to make rectangles more even-sized |
| **SplitShorterAxis** | Cut along shorter dimension of the free rectangle |
| **SplitLongerAxis** | Cut along longer dimension of the free rectangle |

#### Free Rectangle Choice Heuristics

Same as MaxRects: BestAreaFit, BestShortSideFit, BestLongSideFit, plus "Worst" variants (WorstAreaFit, WorstShortSideFit, WorstLongSideFit) that prefer larger free spaces to keep options open.

#### Characteristics

- **Time complexity**: O(n * free_rects) per insertion, but free list stays smaller than MaxRects
- **Efficiency**: 85-93% typical; slightly worse than MaxRects due to the guillotine constraint
- **Speed**: Faster than MaxRects (fewer free rectangles to manage)
- **Advantage**: Can support deallocation more easily (merge two siblings back into parent)
- **Disadvantage**: The guillotine constraint loses potential packing arrangements
- **Best for**: Dynamic scenarios where glyphs are added/removed (font caches), not static atlas generation

---

## Algorithm Comparison

### Packing Efficiency

| Algorithm | Small Sets (25 rects) | Large Sets (250 rects) | Font Glyphs (typical) |
|-----------|----------------------|------------------------|----------------------|
| MaxRects (BSSF, batch) | 83-88% | 93-97% | 95-98% |
| Skyline (BL) | 74-83% | 93-94% | 92-95% |
| Shelf (BHF) | 68-81% | 86-93% | 88-93% |
| Guillotine (BAF) | 80-86% | 90-95% | 90-94% |

### Speed (relative, for ~200 glyphs)

| Algorithm | Relative Speed | Notes |
|-----------|---------------|-------|
| Shelf | Fastest | < 1ms |
| Skyline | Fast | < 1ms |
| Guillotine | Fast | 1-2ms |
| MaxRects (batch) | Moderate | 2-10ms |

### Comparison Summary

| Property | MaxRects | Shelf | Skyline | Guillotine |
|----------|----------|-------|---------|------------|
| Packing efficiency | Best | Lowest | Good | Good |
| Implementation complexity | High | Low | Medium | Medium |
| Speed | Moderate | Fastest | Fast | Fast |
| Rotation support | Optional | Optional | Optional | Optional |
| Deallocation support | No | No | No | Yes (with tree) |
| Dynamic resizing | Difficult | Easy | Easy | Moderate |
| Best heuristic | BSSF (batch) | BestHeightFit | MinWasteFit | BAF + SplitShorterLeftoverAxis |

### For BMFont-Specific Workloads

Font glyph packing has favorable characteristics:
- Rectangles are generally **small** relative to the bin
- Heights are **relatively uniform** (same font size, ascender/descender range)
- Widths vary moderately ('i' vs 'W' vs 'M')
- Typical glyph counts: 95 (ASCII) to 600+ (extended Latin/Cyrillic)

This means even simpler algorithms (Shelf, Skyline) perform well because the rectangles are similarly sized. MaxRects gains its advantage primarily with highly varied rectangle sizes.

---

## BMFont Format Constraints

### Per-Glyph Fields (from AngelCode BMFont spec)

Each character in the .fnt file stores:
```
char id=65 x=10 y=20 width=12 height=15 xoffset=1 yoffset=2 xadvance=14 page=0 chnl=15
```

| Field | Description | Packing Relevance |
|-------|-------------|-------------------|
| `x`, `y` | Position in texture (integer pixels) | Output of packing algorithm |
| `width`, `height` | Glyph dimensions in texture | Input to packing algorithm |
| `page` | Texture page index (0-based) | Multi-page packing output |
| `chnl` | Channel: 1=B, 2=G, 4=R, 8=A, 15=all | Packed mode channel assignment |

### No Rotation Support

The BMFont format has **no rotation field**. There is no way to indicate that a glyph should be rendered rotated. This means:
- The packing algorithm **must not rotate** rectangles
- All heuristics involving 90-degree rotation are irrelevant
- This simplifies the packing problem but may reduce efficiency by 5-10%

### Padding and Spacing

BMFont defines two concepts:
- **Padding** (up, right, down, left): Extra space around each glyph *within* its rectangle. Used for effects like outlines, drop shadows, SDF margins.
- **Spacing** (horizontal, vertical): Space *between* glyphs in the texture atlas.

For packing, each glyph's effective size is:
```
effective_width  = glyph_width  + padding_left + padding_right
effective_height = glyph_height + padding_top  + padding_bottom
```

And spacing is added between glyphs during placement:
```
placement_step_x = effective_width  + spacing_horizontal
placement_step_y = effective_height + spacing_vertical
```

### Packed Mode (Channel Packing)

When `packed=1` in the common block:
- Monochrome glyphs are packed into **individual RGBA channels**
- Each channel stores independent glyph data
- Effectively **4x the capacity** per texture page
- The `chnl` field indicates which channel(s) contain the glyph

Implementation approach:
1. Run the packing algorithm 4 times (once per channel) or interleave
2. Each channel has its own independent layout
3. Same (x, y, width, height) coordinates can be reused across channels
4. The renderer uses the `chnl` field to extract the correct channel

Constraints of packed mode:
- Only works with monochrome (1-bit or grayscale) glyphs
- Cannot be used with colored/antialiased glyphs that use multiple channels
- Glyph coloring must be done in the shader (multiply by vertex color)

### Power-of-2 Texture Sizes

Recommended: 256, 512, 1024, 2048. While modern GPUs handle NPOT (non-power-of-two), many game engines and older hardware require or prefer POT sizes. The packing algorithm should:
1. Accept a target texture size (POT)
2. Pack as many glyphs as fit
3. Overflow remaining glyphs to additional pages

---

## What Existing Tools Use

### AngelCode BMFont (the original)

BMFont's documentation does not publicly specify its packing algorithm. Based on observed behavior and community analysis, it appears to use a **simple row/shelf-based packer** -- glyphs are sorted by height and packed left-to-right in rows. BMFont predates the Jylanki survey (2010) and was designed for simplicity. It supports packed mode (channel packing) and multiple pages.

### libGDX Texture Packer

- Uses **MaxRects** algorithm (Jylanki's implementation)
- Applies **brute force**: tests multiple heuristics at various sizes, picks the best result
- Supports rotation (90 degrees), padding, power-of-2, multi-page
- Configuration: `paddingX`, `paddingY`, `pot` (power-of-two), `rotation`, `maxWidth`, `maxHeight`
- When images exceed max page size, automatically creates multiple pages
- Open source (Apache 2.0)

### TexturePacker (CodeAndWeb, commercial)

- Primary algorithm: **MaxRects**
- Also offers: Basic (simple row packing), Polygon (non-rectangular, for sprite meshes)
- Supports `Common Divisor` constraint for grid-aligned placement
- Industry standard for sprite sheets since 2010
- Supports rotation, trimming, aliasing (dedup identical images)

### stb_rect_pack (Sean Barrett)

- Uses **Skyline Bottom-Left** algorithm
- Public domain (single-header C library)
- No rotation support
- Used by: Dear ImGui, fontstash, nanovg, and many game engines
- C# port available: StbRectPackSharp (Unlicense/public domain)

### Firefox WebRender

- Uses **Shelf packing** (via the `etagere` Rust crate)
- Chose shelf over guillotine for better fragmentation behavior in dynamic scenarios
- Optimized for runtime glyph caching (add/remove), not static atlas generation

### FreeType + Harfbuzz ecosystem

- No standard packing; each application implements its own
- Common choice: Skyline (via stb_rect_pack) for runtime, MaxRects for offline

---

## Multi-Page Strategy

### When to Overflow to a New Page

A glyph overflows to a new page when:
1. No free rectangle in any existing page can fit the glyph (with padding/spacing)
2. The page has reached a configurable fullness threshold (optional optimization)

### Recommended Strategy

**Greedy fill, minimize page count:**
1. Sort glyphs by height descending (tallest first) -- this improves packing efficiency
2. For each glyph, attempt to place it on the current page using the chosen algorithm
3. If it doesn't fit, start a new page
4. After initial packing, optionally try to move glyphs from the last (least-full) page to earlier pages

**Why minimize page count?**
- Each texture page requires a separate draw call (or texture bind) at render time
- Fewer pages = fewer draw calls = better GPU performance
- Most fonts (ASCII + extended) fit in a single 1024x1024 or 2048x2048 page

### Page Size Selection Heuristic

```
total_glyph_area = sum(glyph_width * glyph_height) for all glyphs
overhead_factor  = 1.15 to 1.30  (15-30% packing overhead)
needed_area      = total_glyph_area * overhead_factor

target_size = smallest POT where target_size^2 >= needed_area
if target_size > max_allowed_size:
    use max_allowed_size with multiple pages
```

### Glyph Sorting for Better Packing

Pre-sorting glyphs before insertion significantly improves results:

| Sort Order | Effectiveness | Notes |
|------------|--------------|-------|
| Height descending | Best overall | Standard recommendation |
| Area descending | Good | Better for varied aspect ratios |
| Width descending | Fair | Good for shelf packing |
| Perimeter descending | Good | Accounts for both dimensions |
| Max(w,h) descending | Good | Simple, effective |

Height-descending is the standard recommendation for font glyphs because it creates uniform shelf heights.

---

## Existing C# Implementations

### NuGet Packages

| Package | Algorithm(s) | License | Notes |
|---------|-------------|---------|-------|
| **RectangleBinPack.CSharp** (1.0.4) | MaxRects, Skyline, Guillotine, Shelf | Public domain | Complete port of Jylanki's C++ code. All heuristics included. |
| **RectpackSharp** (1.2.0) | Custom (based on rectpack-2D) | MIT | .NET Standard. Simple API: `RectanglePacker.Pack()`. |
| **StbRectPackSharp** | Skyline Bottom-Left | Public domain (Unlicense) | Port of stb_rect_pack.h. Minimal, zero-allocation design. |
| **RectangleBinPacking** (1.0.2) | MaxRects, Shelf | Unknown | Older package, less maintained. |

### GitHub Projects (No NuGet)

| Project | Algorithm | License | Notes |
|---------|-----------|---------|-------|
| Unity MaxRectsBinPack | MaxRects (all 5 heuristics) | Public domain | Widely used C# port, originally from Unity wiki |
| Various gists/forks | MaxRects | Public domain | Many standalone C# files available |

### Recommendation for bmfontier

**Best option: RectangleBinPack.CSharp** or **implement MaxRects directly**.

Rationale:
- The algorithm is well-documented (Jylanki's paper + reference C++ code)
- Public domain license (no attribution requirements, no copyleft)
- ~300-400 lines of C# for a complete MaxRects implementation
- Can be embedded directly (no external dependency) for a self-contained tool

If preferring a lighter dependency:
- **StbRectPackSharp** (Skyline) is simpler, faster, and public domain
- Slightly lower packing efficiency but excellent for font glyphs

---

## Recommendation for bmfontier

### Primary Algorithm: MaxRects with BestShortSideFit

**Why MaxRects BSSF:**
1. **Best packing efficiency** -- critical for minimizing texture page count
2. **Industry standard** -- used by libGDX, TexturePacker, and most atlas tools
3. **Well-understood** -- extensive documentation, reference implementations, public domain code
4. **No rotation needed** -- simplifies implementation (BMFont has no rotation field)
5. **Batch mode** -- evaluate all remaining glyphs at each step for optimal global placement

### Implementation Plan

```
1. Pre-processing:
   - Calculate effective glyph sizes (glyph + padding)
   - Sort glyphs by height descending
   - Estimate page size (smallest POT fitting ~85% of total area)

2. Packing loop:
   - Initialize MaxRects bin at target page size
   - Use batch insertion (globally best placement per step)
   - Heuristic: BestShortSideFit (fallback: try all 5, pick best occupancy)
   - When a glyph won't fit: start new page, continue

3. Post-processing:
   - Record (x, y, width, height, page) per glyph
   - Optionally try smaller page size if last page is mostly empty
   - For packed mode: run 4 independent packing passes (one per channel)

4. Output:
   - Glyph coordinates in BMFont format
   - Texture page images
```

### Optional: Offer Skyline as Fast Alternative

For users who want faster generation (e.g., iterating on font settings):
- Skyline Bottom-Left is 2-5x faster
- 2-5% worse packing efficiency
- Much simpler implementation (~150 lines)
- Could be the default for preview mode, with MaxRects for final export

### Channel Packing (Packed Mode) Strategy

When packed mode is enabled:
1. Divide glyphs into 4 groups (round-robin or by size optimization)
2. Run the packing algorithm independently for each channel
3. Composite the 4 single-channel results into one RGBA texture
4. Set the `chnl` field per glyph (1=B, 2=G, 4=R, 8=A)

Optimization: pack the largest glyphs first across all 4 channels to balance utilization.

---

## References

### Papers and Surveys
- Jukka Jylanki, "A Thousand Ways to Pack the Bin - A Practical Approach to Two-Dimensional Rectangle Bin Packing" (2010) -- https://core.ac.uk/outputs/103387426/
- Reference C++ implementation: https://github.com/juj/RectangleBinPack

### Algorithm Explorations
- David Colson, "Exploring Rectangle Packing Algorithms" (2020) -- https://www.david-colson.com/2020/03/10/exploring-rect-packing.html
- Nicolas Silva, "Eight million pixels and counting - Improving texture atlas allocation in WebRender" -- https://nical.github.io/posts/etagere.html
- "Texture Packing for Fonts" -- https://straypixels.net/texture-packing-for-fonts/
- Julien Vernay, "Skyline algorithm for packing 2D rectangles" -- https://jvernay.fr/en/blog/skyline-2d-packer/implementation/

### BMFont Format
- AngelCode BMFont file format spec: https://www.angelcode.com/products/bmfont/doc/file_format.html
- AngelCode BMFont tool: https://www.angelcode.com/products/bmfont/

### Tools
- libGDX Texture Packer: https://libgdx.com/wiki/tools/texture-packer
- TexturePacker (CodeAndWeb): https://www.codeandweb.com/texturepacker
- stb_rect_pack.h: https://github.com/nothings/stb/blob/master/stb_rect_pack.h

### C# Implementations
- RectangleBinPack.CSharp (NuGet): https://libraries.io/nuget/RectangleBinPack.CSharp
- RectpackSharp: https://github.com/ThomasMiz/RectpackSharp
- StbRectPackSharp: https://github.com/StbSharp/StbRectPackSharp
- Unity MaxRectsBinPack C# port: https://forum.unity.com/threads/maxrectsbinpack-c-port-useful-for-creating-texture-atlases.130376/

### Benchmark Data Source
- David Colson's benchmarks (2020): Skyline 93-94% at 250 rects, Row packing 85-93%, with speed ranging from <1ms (shelf/skyline) to 1000+ms (pixel scanning)
