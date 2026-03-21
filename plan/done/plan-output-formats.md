# KernSmith -- Output Formats

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md), [Texture Packing](plan-texture-packing.md)

> Types used in this document: `OutputFormat`, `BmFontResult`, `AtlasPage` are defined in [plan-data-types.md](plan-data-types.md). The model types (`BmFontModel`, `InfoBlock`, `CommonBlock`, `PageEntry`, `CharEntry`, `KerningEntry`) are defined below in this document.

---

## BmFontModel (In-Memory Model)

The core data structure that the entire pipeline targets. All output formats are serialized from this model.

```csharp
public class BmFontModel
{
    public InfoBlock Info { get; set; }
    public CommonBlock Common { get; set; }
    public List<PageEntry> Pages { get; set; }
    public List<CharEntry> Characters { get; set; }
    public List<KerningEntry> KerningPairs { get; set; }
}

public record InfoBlock(
    string Face, int Size, bool Bold, bool Italic,
    string Charset, bool Unicode, int StretchH,
    bool Smooth, int Aa,
    Padding Padding, Spacing Spacing, int Outline);

public record CommonBlock(
    int LineHeight, int Base, int ScaleW, int ScaleH,
    int Pages, bool Packed,
    int AlphaChnl, int RedChnl, int GreenChnl, int BlueChnl);

public record PageEntry(int Id, string File);

public record CharEntry(
    int Id, int X, int Y, int Width, int Height,
    int XOffset, int YOffset, int XAdvance,
    int Page, int Channel);

public record KerningEntry(int First, int Second, int Amount);
```

---

## IBmFontFormatter Interface

Output formatting is abstracted behind `IBmFontFormatter` so new formats can be added by implementing the interface:

> `IBmFontFormatter`, `IBmFontTextFormatter`, and `IBmFontBinaryFormatter` interfaces are defined in [plan-data-types.md](plan-data-types.md#interfaces).

Built-in implementations:
- **`TextFormatter`** -- implements `IBmFontTextFormatter`, produces BMFont text format.
- **`XmlFormatter`** -- implements `IBmFontTextFormatter`, produces BMFont XML format.
- **`BmFontBinaryFormatter`** -- implements `IBmFontBinaryFormatter`, produces BMFont binary format.

Adding a new format (e.g., JSON, custom binary, protobuf) requires only implementing `IBmFontTextFormatter` or `IBmFontBinaryFormatter`.

---

## Output Delegation

`BmFontResult` delegates to the formatters:

```csharp
public class BmFontResult
{
    // .ToString() delegates to TextFormatter
    public override string ToString()
        => new TextFormatter().FormatToString(Model);

    // .ToXml() delegates to XmlFormatter
    public string ToXml()
        => new XmlFormatter().FormatToString(Model);

    // .ToBinary() delegates to BmFontBinaryFormatter
    public byte[] ToBinary()
        => new BmFontBinaryFormatter().FormatToBytes(Model);

    // .ToFile() uses FileWriter which combines formatter + atlas encoder
    public void ToFile(string basePath, OutputFormat format = OutputFormat.Text)
        => new FileWriter().Write(this, basePath, format);
}
```

> The full `BmFontResult` type definition (including `Model` and `Pages` properties) is in [plan-data-types.md](plan-data-types.md#result-types).

> `OutputFormat` enum is defined in [plan-data-types.md](plan-data-types.md). `ToFile(path, OutputFormat.Binary)` selects the formatter for the `.fnt` file. The atlas images are always PNG regardless of output format.

---

## Text Format Output

The default BMFont text format. Each line is a tag followed by key=value pairs.

### Text Format Serialization Rules

- Each line starts with a tag name (`info`, `common`, `page`, `chars`, `char`, `kernings`, `kerning`)
- Fields are `key=value` pairs separated by spaces
- String values are double-quoted: `face="Arial"`
- Boolean values serialize as `0` or `1`
- `Padding` serializes as `up,right,down,left` (comma-separated, no spaces): `padding=2,2,2,2`
- `Spacing` serializes as `horiz,vert`: `spacing=1,1`
- `chars count=N` and `kernings count=N` summary lines are required before their respective entries
- Field ordering follows the conventional BMFont order shown in the example below

```
info face="Arial" size=32 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=1,1 outline=0
common lineHeight=32 base=26 scaleW=256 scaleH=256 pages=1 packed=0 alphaChnl=0 redChnl=4 greenChnl=4 blueChnl=4
page id=0 file="arial_0.png"
chars count=95
char id=65 x=10 y=20 width=18 height=22 xoffset=1 yoffset=4 xadvance=20 page=0 chnl=15
...
kernings count=42
kerning first=65 second=86 amount=-2
...
```

---

## XML Format Output

Standard XML with `<?xml version="1.0"?>` declaration, `<font>` root element, same field names as text format but as XML attributes.

### XML Format Rules

- XML declaration: `<?xml version="1.0"?>`
- Root element: `<font>`
- Leaf elements use self-closing tags: `<info ... />`, `<char ... />`
- All attribute values are quoted strings (even numeric, per XML spec)
- `<chars>` and `<kernings>` wrapper elements include a `count` attribute

```xml
<?xml version="1.0"?>
<font>
  <info face="Arial" size="32" bold="0" italic="0" charset="" unicode="1"
        stretchH="100" smooth="1" aa="1" padding="0,0,0,0" spacing="1,1" outline="0"/>
  <common lineHeight="32" base="26" scaleW="256" scaleH="256" pages="1" packed="0"
          alphaChnl="0" redChnl="4" greenChnl="4" blueChnl="4"/>
  <pages>
    <page id="0" file="arial_0.png"/>
  </pages>
  <chars count="95">
    <char id="65" x="10" y="20" width="18" height="22"
          xoffset="1" yoffset="4" xadvance="20" page="0" chnl="15"/>
    ...
  </chars>
  <kernings count="42">
    <kerning first="65" second="86" amount="-2"/>
    ...
  </kernings>
</font>
```

---

## Binary Format Output

- **Header**: `BMF` (3 bytes) + version `3` (1 byte).
- **Block 1 (Info)**: type `1` (1 byte) + block size (4 bytes, little-endian) + 14 bytes fixed fields + null-terminated font name.
- **Block 2 (Common)**: type `2` + block size + 15 bytes fixed fields.
- **Block 3 (Pages)**: type `3` + block size + concatenated null-terminated page filenames.
- **Block 4 (Chars)**: type `4` + block size + 20 bytes per character.
- **Block 5 (Kerning)**: type `5` + block size + 10 bytes per pair (optional, omitted if no kerning).
- All multi-byte values are **little-endian** (unlike TTF tables which are big-endian).

### Binary Format Implementation Reference

The binary format uses a block-based structure: magic bytes `BMF` + version `3`, then 5 typed blocks.

For the complete byte-level field layouts needed during implementation:
- **Block 1 (Info) byte offsets and types**: See [REF-05-bmfont-format-reference.md](../reference/REF-05-bmfont-format-reference.md), "Block 1: Info" section
- **Block 2 (Common) byte offsets**: See REF-05-bmfont-format-reference.md, "Block 2: Common" section
- **Block 3 (Pages)**: See REF-05-bmfont-format-reference.md, "Block 3: Pages" section
- **Block 4 (Chars)**: See REF-05-bmfont-format-reference.md, "Block 4: Characters" section
- **Block 5 (Kerning Pairs)**: See REF-05-bmfont-format-reference.md, "Block 5: Kerning Pairs" section

Key implementation notes:
- `InfoBlock.Bold`, `.Italic`, `.Unicode`, `.Smooth` pack into a single bitfield byte (bits 3, 2, 1, 0 respectively)
- `CommonBlock.Packed` is bit 7 of its bitfield byte
- `fontSize` in Block 1 is `int16` (signed, negative for SDF/smooth)
- `xoffset`, `yoffset`, `xadvance` in Block 4 are `int16` (signed)
- `x`, `y`, `width`, `height` in Block 4 are `uint16`
- `CharEntry.Channel` values: 1=blue, 2=green, 4=red, 8=alpha, 15=all channels

---

## FileWriter

`FileWriter` handles writing the complete output to disk:

1. Format the `BmFontModel` using the specified formatter (text, XML, or binary).
2. Write the `.fnt` file to `{basePath}.fnt`.
3. For each atlas page, encode the bitmap using `IAtlasEncoder` and write to `{basePath}_{pageIndex}.png`.

```csharp
public class FileWriter
{
    public void Write(BmFontResult result, string basePath, OutputFormat format = OutputFormat.Text)
    {
        // Write .fnt descriptor
        // Write .png atlas pages
    }
}
```

The page filenames in the `.fnt` descriptor match the written `.png` filenames (e.g., `myfont_0.png`, `myfont_1.png`).

---

## Error Handling

- Formatters assume valid `BmFontModel` data. Validation happens at pipeline entry (`BmFont.Generate()`), not in formatters.
- `FileWriter` creates directories if they don't exist. Overwrites existing files without warning.
- Page filenames follow the pattern `{fontName}_{pageIndex}.png` (e.g., `Arial_0.png`).
