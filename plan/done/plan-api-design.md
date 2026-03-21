# KernSmith -- API Design

> Part of the [Master Plan](master-plan.md).
> Related: [Project Structure](plan-project-structure.md), [Font Parsing](plan-font-parsing.md), [Rasterization](plan-rasterization.md), [Texture Packing](plan-texture-packing.md), [Output Formats](plan-output-formats.md)

> All intermediate data types, configuration types, and the error handling strategy are defined in [plan-data-types.md](plan-data-types.md).

---

## Design Principle: Modularity

Every major component is behind an interface so implementations can be swapped without changing the pipeline. The default `BmFont.Generate()` wires sensible defaults, but every piece is replaceable.

### Core Interfaces

> All interface definitions (`IFontReader`, `IRasterizer`, `IGlyphPostProcessor`, `IAtlasPacker`, `IAtlasEncoder`, `IBmFontFormatter`, `ISystemFontProvider`) are defined in [plan-data-types.md](plan-data-types.md#interfaces).

---

## How `BmFont.Generate()` Wires Defaults

```csharp
public static class BmFont
{
    public static BmFontResult Generate(string fontPath, int size)
        => Generate(fontPath, new FontGeneratorOptions { Size = size });

    public static BmFontResult Generate(string fontPath, FontGeneratorOptions options)
    {
        byte[] fontData = File.ReadAllBytes(fontPath);
        return Generate(fontData, options);
    }

    public static BmFontResult Generate(byte[] fontData, FontGeneratorOptions options)
    {
        // 1. Parse font
        var fontReader = options.FontReader ?? new TtfFontReader();
        var fontInfo = fontReader.ReadFont(fontData, options.FaceIndex);

        // 2. Resolve character set
        var codepoints = options.Characters.Resolve(fontInfo.AvailableCodepoints);

        // 3. Rasterize glyphs
        using var rasterizer = options.Rasterizer ?? new FreeTypeRasterizer();
        rasterizer.LoadFont(fontData, options.FaceIndex);
        var rasterOptions = RasterOptions.FromGeneratorOptions(options);
        var glyphs = rasterizer.RasterizeAll(codepoints, rasterOptions);

        // 4. Apply post-processors
        var postProcessors = options.PostProcessors ?? Array.Empty<IGlyphPostProcessor>();
        foreach (var processor in postProcessors)
            glyphs = glyphs.Select(g => processor.Process(g)).ToList();

        // 5. Pack into atlas
        var packer = options.Packer ?? CreatePacker(options.PackingAlgorithm);
        var glyphRects = glyphs.Select(g => CreateRect(g, options.Padding, options.Spacing)).ToList();
        int pageSize = EstimatePageSize(glyphRects, options.MaxTextureSize);
        var packResult = packer.Pack(glyphRects, pageSize, pageSize);

        // 6. Build atlas pages
        var encoder = options.AtlasEncoder ?? new StbPngEncoder();
        var pages = AtlasBuilder.Build(glyphs, packResult, options.Padding, encoder);

        // 7. Assemble BMFont model
        var model = BmFontModelBuilder.Build(fontInfo, glyphs, packResult, options);

        return new BmFontResult(model, pages);
    }

    private static IAtlasPacker CreatePacker(PackingAlgorithm algorithm) => algorithm switch
    {
        PackingAlgorithm.MaxRects => new MaxRectsPacker(),
        PackingAlgorithm.Skyline => new SkylinePacker(),
        _ => new MaxRectsPacker(),
    };
}
```

---

## Builder Pattern Alternative

For users who prefer fluent configuration over the options object:

```csharp
var result = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(32)
    .WithCharacters(CharacterSet.Ascii)
    .WithRasterizer(new FreeTypeRasterizer())
    .WithPacker(new MaxRectsPacker())
    .WithEncoder(new StbPngEncoder())
    .WithPostProcessor(new OutlinePostProcessor(width: 2))
    .WithPostProcessor(new SdfPostProcessor())
    .WithMaxTextureSize(1024)
    .WithPadding(2, 2, 2, 2)
    .WithKerning(true)           // Matches FontGeneratorOptions.Kerning
    .Build();
```

The builder delegates to the same pipeline as `BmFont.Generate()`. It populates a `FontGeneratorOptions` internally and calls the same code path.

---

## Simple Usage (Minimal Code)

```csharp
// Generate BMFont from a TTF file -- all defaults
var result = BmFont.Generate("path/to/font.ttf", size: 32);

// Get the .fnt content as string
string fntContent = result.ToString();

// Get atlas page as PNG bytes
byte[] pngBytes = result.Pages[0].ToPng();

// Write everything to disk
result.ToFile("output/myfont");
// Creates: output/myfont.fnt + output/myfont_0.png
```

> `AtlasPage.ToPng()` is a convenience method that uses the default `StbPngEncoder`. For custom encoding, use `AtlasPage.PixelData` directly with any `IAtlasEncoder` implementation.

---

## In-Memory Usage (Game Engine, No Disk)

```csharp
byte[] fontBytes = LoadFontFromSomewhere();

var result = BmFont.Generate(fontBytes, new FontGeneratorOptions
{
    Size = 24,
    Characters = CharacterSet.Ascii,
    MaxTextureSize = 512,
    Padding = new Padding(1),    // Padding(int all) convenience constructor
    Spacing = new Spacing(1),    // Spacing(int both) convenience constructor
});

// Use directly -- never touches disk
string fntData = result.ToString();
byte[][] atlasPages = result.Pages.Select(p => p.ToPng()).ToArray();
```

---

## Advanced Usage

```csharp
var result = BmFont.Generate("font.ttf", new FontGeneratorOptions
{
    Size = 48,
    Characters = CharacterSet.FromRanges((0x20, 0x7E), (0x400, 0x4FF)), // ASCII + Cyrillic
    Bold = false,
    Italic = false,
    AntiAlias = AntiAliasMode.Grayscale,
    MaxTextureSize = 1024,
    Padding = new Padding(2, 2, 2, 2),
    Spacing = new Spacing(1, 1),
    PackingAlgorithm = PackingAlgorithm.MaxRects,
    Kerning = true,
    Outline = 0,
    Sdf = false,
});

// Different output formats
string textFormat = result.ToString();           // BMFont text format
string xmlFormat = result.ToXml();               // BMFont XML format
byte[] binaryFormat = result.ToBinary();         // BMFont binary format

// System font loading
var result2 = BmFont.GenerateFromSystem("Arial", size: 16);
```

---

## Entry Point Overloads

```csharp
public static class BmFont
{
    // From file path
    public static BmFontResult Generate(string fontPath, int size);
    public static BmFontResult Generate(string fontPath, FontGeneratorOptions options);

    // From byte array (in-memory, zero disk I/O)
    public static BmFontResult Generate(byte[] fontData, FontGeneratorOptions options);
    public static BmFontResult Generate(byte[] fontData, int size);

    // From stream
    public static BmFontResult Generate(Stream fontStream, FontGeneratorOptions options);

    // From system font name
    public static BmFontResult GenerateFromSystem(string fontFamily, int size);
    public static BmFontResult GenerateFromSystem(string fontFamily, FontGeneratorOptions options);

    // Builder pattern
    public static BmFontBuilder Builder();
}
```

---

## Result Type

```csharp
public class BmFontResult
{
    public BmFontModel Model { get; }           // In-memory BMFont data
    public IReadOnlyList<AtlasPage> Pages { get; }  // Atlas page bitmaps

    // Output methods
    public override string ToString();           // BMFont text format
    public string ToXml();                       // BMFont XML format
    public byte[] ToBinary();                    // BMFont binary format

    // Disk output
    public void ToFile(string basePath);         // Write .fnt + .png files
    public void ToFile(string basePath, OutputFormat format);
}
```

> Custom formatters are NOT injected via `FontGeneratorOptions`. Instead, users access `BmFontResult.Model` and call their custom formatter directly:
> ```csharp
> var result = BmFont.Generate(fontData, options);
> var customOutput = myFormatter.Format(result.Model);
> ```

---

## Configuration Types

```csharp
public class FontGeneratorOptions
{
    public int Size { get; set; } = 32;
    public CharacterSet Characters { get; set; } = CharacterSet.Ascii;
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;
    public int MaxTextureSize { get; set; } = 1024;
    public Padding Padding { get; set; } = new Padding(0, 0, 0, 0);
    public Spacing Spacing { get; set; } = new Spacing(1, 1);
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;
    public bool Kerning { get; set; } = true;
    public int Outline { get; set; } = 0;
    public bool Sdf { get; set; } = false;
    public bool PowerOfTwo { get; set; } = true;
    public int Dpi { get; set; } = 72;
    public int FaceIndex { get; set; } = 0;     // For .ttc font collections

    // Swappable components (null = use defaults)
    public IFontReader? FontReader { get; set; }
    public IRasterizer? Rasterizer { get; set; }
    public IAtlasPacker? Packer { get; set; }
    public IAtlasEncoder? AtlasEncoder { get; set; }
    public ISystemFontProvider? SystemFontProvider { get; set; }
    public IReadOnlyList<IGlyphPostProcessor>? PostProcessors { get; set; }
}

```

> See [plan-data-types.md](plan-data-types.md#configuration-types) for `AntiAliasMode`, `PackingAlgorithm`, `OutputFormat` enum definitions and `Padding` and `Spacing` definitions (includes convenience constructors).

---

## CharacterSet

```csharp
public class CharacterSet
{
    // Predefined sets
    public static CharacterSet Ascii { get; }           // U+0020..U+007E (95 chars)
    public static CharacterSet ExtendedAscii { get; }   // U+0020..U+00FF (224 chars)
    public static CharacterSet Latin { get; }            // ASCII + Latin Extended-A/B

    // Custom construction
    public static CharacterSet FromRanges(params (int start, int end)[] ranges);
    public static CharacterSet FromChars(string characters);
    public static CharacterSet FromChars(IEnumerable<int> codepoints);

    // Combination
    public static CharacterSet Union(params CharacterSet[] sets);

    // Enumeration
    public IEnumerable<int> GetCodepoints();
    public int Count { get; }

    // Resolution (filter to what the font actually supports)
    public IEnumerable<int> Resolve(IReadOnlyList<int> availableCodepoints);
}
```
