# bmfontier -- Output Formats

> Part of the [Master Plan](master-plan.md).
> Related: [API Design](plan-api-design.md), [Texture Packing](plan-texture-packing.md)

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

```csharp
public interface IBmFontFormatter
{
    /// Format identifier (e.g., "text", "xml", "binary").
    string Format { get; }
}

/// Formatter that produces string output (text, XML).
public interface IBmFontTextFormatter : IBmFontFormatter
{
    string FormatToString(BmFontModel model);
}

/// Formatter that produces binary output.
public interface IBmFontBinaryFormatter : IBmFontFormatter
{
    byte[] FormatToBytes(BmFontModel model);
}
```

Built-in implementations:
- **`TextFormatter`** -- implements `IBmFontTextFormatter`, produces BMFont text format.
- **`XmlFormatter`** -- implements `IBmFontTextFormatter`, produces BMFont XML format.
- **`BinaryFormatter`** -- implements `IBmFontBinaryFormatter`, produces BMFont binary format.

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

    // .ToBinary() delegates to BinaryFormatter
    public byte[] ToBinary()
        => new BinaryFormatter().FormatToBytes(Model);

    // .ToFile() uses FileWriter which combines formatter + atlas encoder
    public void ToFile(string basePath, OutputFormat format = OutputFormat.Text)
        => new FileWriter().Write(this, basePath, format);
}
```

---

## Text Format Output

The default BMFont text format. Each line is a tag followed by key=value pairs:

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

Standard XML with `<?xml version="1.0"?>` declaration, `<font>` root element, same field names as text format but as XML attributes:

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
