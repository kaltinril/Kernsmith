# bmfontier -- Font Parsing

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md), [Rasterization](plan-rasterization.md)

---

## IFontReader Interface

Font reading is abstracted behind `IFontReader` so the source format can be swapped:

```csharp
public interface IFontReader
{
    FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
}
```

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

We do NOT need: MarkBase, MarkLig, MarkMark, ContextPos, ChainContextPos, Extension, or GSUB lookups.

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
    public IReadOnlyDictionary<(int first, int second), int> GetKerningPairs(int unitsPerEm, int targetSize);
}
```

`TtfParser` implements the parsing logic used by `TtfFontReader` (the default `IFontReader` implementation). Each table parser is a separate class in `Bmfontier.Font.Tables`.

All table parsers are **lazy**: they parse on first access, not at construction time.

---

## Binary Reading Conventions

- TTF/OTF tables are **big-endian**. Use `BinaryPrimitives.ReadInt16BigEndian()` / `ReadUInt32BigEndian()` from `System.Buffers.Binary`.
- Operate on `ReadOnlySpan<byte>` for zero-allocation parsing.
- All table parsers are lazy: parse on first access, not at construction time.
