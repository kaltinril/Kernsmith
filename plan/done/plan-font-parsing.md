# KernSmith -- Font Parsing

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md), [Rasterization](plan-rasterization.md)

---

## Data Types

All types used in this document (`FontInfo`, `KerningPair`, `HeadTable`, `HheaTable`, `Os2Metrics`, `NameInfo`) are defined in [plan-data-types.md](plan-data-types.md).

---

## IFontReader Interface

Font reading is abstracted behind `IFontReader` so the source format can be swapped:

> `IFontReader` interface is defined in [plan-data-types.md](plan-data-types.md#interfaces).

The default implementation, `TtfFontReader`, combines FreeTypeSharp (for metrics/rasterization setup) with our `TtfParser` (for tables FreeTypeSharp cannot access). Tomorrow, someone could add `WoffFontReader`, `OtfFontReader`, or any other implementation without changing the pipeline.

---

## FreeTypeSharp Usage

### What We Use FreeTypeSharp For

| Capability | FreeType API | Notes |
|------------|-------------|-------|
| Load font from file | `FT_New_Face` | |
| Load font from memory | `FT_New_Memory_Face` | Critical for in-memory pipeline |
| Set size | `FT_Set_Char_Size`, `FT_Set_Pixel_Sizes` | Configurable size + DPI |
| Glyph rasterization | `FT_Load_Glyph` + `FT_Render_Glyph` | Core rasterization |
| Anti-alias modes | `FT_Render_Mode_` enum | NORMAL, LIGHT, MONO, LCD, SDF |
| Glyph bitmap access | `FT_Bitmap_` struct fields | buffer, rows, width, pitch, pixel_mode |
| Per-glyph metrics | `FT_Glyph_Metrics_` | width, height, horiBearingX/Y, horiAdvance |
| Font-level metrics | `FT_FaceRec_`, `FT_Size_Metrics_` | ascender, descender, height, units_per_EM |
| Face name | `FT_FaceRec_.family_name`, `style_name` | Manual byte pointer marshaling |
| Style flags | `FT_STYLE_FLAG` | Bold, italic detection |
| Kern table kerning | `FT_Get_Kerning` | Only reads kern table, NOT GPOS |
| Char-to-glyph mapping | `FT_Get_Char_Index` | Unicode codepoint to glyph index |
| Enumerate chars | `FT_Get_First_Char` / `FT_Get_Next_Char` | Discover all available codepoints |
| Font collections | `FT_New_Face` with face_index, `num_faces` | .ttc support |
| Synthetic bold | `FT_GlyphSlot_Embolden` | |
| Synthetic italic | `FT_GlyphSlot_Oblique` | |
| SDF rendering | `FT_RENDER_MODE_SDF` | FreeType 2.13+ |
| Stroking (outlines) | `FT_Stroker_*` family | Outline generation |

### What FreeTypeSharp Cannot Do (We Build Ourselves)

| Gap | Impact | Our Solution |
|-----|--------|-------------|
| **GPOS kerning** | **Critical** -- most modern fonts store kerning exclusively in GPOS, not kern table. `FT_Get_Kerning` returns 0 for these. | Our GPOS table parser extracts pair positioning data. |
| **SFNT table access** (`FT_Get_Sfnt_Table` not bound) | Cannot read OS/2 table (typo metrics, weight class, x-height, cap height, panose, Unicode ranges). | Our OS/2 table parser. |
| **Name table strings** (`FT_Get_Sfnt_Name` not bound) | Cannot read copyright, designer, description, license. | Our name table parser. |
| **Variable font axes** (`FT_Get_MM_Var` not bound) | Cannot enumerate or set variation axes. | Our own parser (Phase 2). |
| **System font enumeration** | FreeType does not discover fonts. | Our cross-platform font directory scanner (~200-400 lines), behind `ISystemFontProvider`. |

### FreeTypeSharp Risk Mitigation

- **Single-maintainer risk**: FreeTypeSharp has 83 stars, 1 active maintainer. If abandoned:
  - The P/Invoke layer is thin (~200 functions) and auto-generated -- easy to fork or maintain.
  - The native FreeType binary is the real asset; the C# wrapper is replaceable.
  - We can add missing P/Invoke bindings ourselves (e.g., `FT_Get_Sfnt_Table`) with trivial `[DllImport]` declarations.
- **FreeType itself**: Extremely stable, 25+ years of development, used in Linux/Android/Chrome.

---

## Our TTF Parser -- Scope

We parse only the tables FreeTypeSharp cannot expose. We do NOT parse glyph outlines (glyf/CFF) or implement rasterization.

### Tables to Parse

| Table | Priority | Complexity | Purpose |
|-------|----------|------------|---------|
| `head` | Phase 1 | Simple | Units-per-EM, bounding box, index-to-loc format |
| `hhea` | Phase 1 | Simple | Ascender, descender, line gap, number of h-metrics |
| `hmtx` | Phase 1 | Simple | Per-glyph advance widths and left side bearings |
| `maxp` | Phase 1 | Simple | Number of glyphs |
| `OS/2` | Phase 1 | Simple | Weight class, typo metrics, x-height, cap height, panose, Unicode ranges, fsSelection |
| `name` | Phase 1 | Simple | Font family name, style, copyright, description |
| `cmap` | Phase 1 | Medium | Character-to-glyph mapping (format 4 and 12 are essential) |
| `kern` | Phase 1 | Medium | Legacy kerning pairs (format 0) |
| `GPOS` | Phase 1 | Hard | OpenType kerning and positioning (PairPos subtable, format 1 and 2) |
| `fvar` | Phase 2 | Medium | Variable font axes (Phase 2) |

---

## GPOS Parser -- The Critical Piece

The GPOS table is the most complex parser we need. For BMFont kerning, we only need a subset:

1. **Lookup type 2**: PairPos (pair adjustment positioning) -- this is where kerning lives in GPOS.
2. **PairPos format 1**: Individual pair adjustments (explicit glyph ID pairs).
3. **PairPos format 2**: Class-based pair adjustments (glyph classes with shared kerning values).
4. We need to resolve glyph IDs back to Unicode codepoints via the cmap table.

We do NOT need: MarkBase, MarkLig, MarkMark, ContextPos, ChainContextPos, or GSUB lookups.

> **Extension Lookups (Type 9)** must be supported. Extension is simply a wrapper that allows subtable offsets beyond 16 bits. Many real-world fonts wrap PairPos lookups in Extension lookups. The parser must unwrap Extension to reach the inner PairPos subtable. This is trivial: read the ExtensionSubstFormat1 header (format: uint16, extensionLookupType: uint16, extensionOffset: uint32), then parse the inner subtable at the given offset.

---

## Parser Architecture

```csharp
// TtfParser reads the font's table directory, then delegates to individual table parsers.
public class TtfParser
{
    public TtfParser(byte[] fontData);
    public TtfParser(ReadOnlySpan<byte> fontData);

    // Table access
    public HeadTable? Head { get; }
    public HheaTable? Hhea { get; }
    public HmtxTable? Hmtx { get; }
    public MaxpTable? Maxp { get; }
    public Os2Table?  Os2  { get; }
    public NameTable? Name { get; }
    public CmapTable? Cmap { get; }
    public KernTable? Kern { get; }
    public GposTable? Gpos { get; }

    // Convenience: merged kerning from kern + GPOS
    public IReadOnlyDictionary<(int first, int second), int> GetKerningPairs();
}
```

`TtfParser` implements the parsing logic used by `TtfFontReader` (the default `IFontReader` implementation). Each table parser is a separate class in `KernSmith.Font.Tables`.

> Merge strategy: Start with kern table pairs. Then apply GPOS pairs — if a pair exists in both kern and GPOS, GPOS takes precedence (per OpenType spec, GPOS supersedes kern). The `GetKerningPairs` method should NOT take `unitsPerEm` as a parameter — it already has access to the `head` table. Return values in font units; the caller scales to pixels.

All table parsers are **lazy**: they parse on first access, not at construction time.

---

## Binary Reading Conventions

- TTF/OTF tables are **big-endian**. Use `BinaryPrimitives.ReadInt16BigEndian()` / `ReadUInt32BigEndian()` from `System.Buffers.Binary`.
- Operate on `ReadOnlySpan<byte>` for zero-allocation parsing.
- All table parsers are lazy: parse on first access, not at construction time.

---

## Error Handling

- Missing tables: Return `null` for optional tables (`Os2Metrics?`, `NameInfo?`). `head` and `cmap` are required — throw `FontParsingException` if missing.
- Malformed table data: Throw `FontParsingException` with the table tag and byte offset.
- Invalid font file (bad magic number): Throw `FontParsingException("Not a valid TrueType/OpenType font")`.
- TTC handling: `TtfParser` must handle the TTC header to locate the correct table directory offset for a given `faceIndex`. FreeTypeSharp handles this internally for its own loading.

---

## FreeType Memory Management

- `FT_Library` and `FT_Face` are native resources requiring explicit cleanup via `IDisposable`.
- Font data passed to `FT_New_Memory_Face` must remain pinned for the lifetime of the face. Use `GCHandle.Alloc(fontData, GCHandleType.Pinned)` and free in `Dispose()`.
- Do NOT use `FreeTypeFaceFacade` — it has a known memory leak. Manage `FT_Library`/`FT_Face` lifecycle directly.
- FreeType is thread-safe only with independent library+face instances per thread.

---

## Implementation References

For byte-level table structures needed during implementation:

- **Table directory / sfnt header**: See [REF-03-ttf-font-reference.md](../reference/REF-03-ttf-font-reference.md), "File Structure" section
- **head table layout**: See REF-03-ttf-font-reference.md, "head — Font Header" section
- **hhea table layout**: See REF-03-ttf-font-reference.md, "hhea — Horizontal Header" section
- **hmtx table layout**: See REF-03-ttf-font-reference.md, "hmtx — Horizontal Metrics" section
- **maxp table layout**: See REF-03-ttf-font-reference.md, "maxp — Maximum Profile" section
- **OS/2 table layout**: See REF-03-ttf-font-reference.md, "OS/2 — OS/2 and Windows Metrics" section
- **name table layout**: See REF-03-ttf-font-reference.md, "name — Naming Table" section
- **cmap table (format 4, 12)**: See REF-03-ttf-font-reference.md, "cmap — Character to Glyph Mapping" section
- **kern table layout**: See REF-03-ttf-font-reference.md, "kern — Kerning" section
- **GPOS table navigation chain**: See REF-03-ttf-font-reference.md, "GPOS — Glyph Positioning" section
- **FreeTypeSharp API surface**: See [REF-02-freetypesharp-evaluation.md](../reference/REF-02-freetypesharp-evaluation.md)
