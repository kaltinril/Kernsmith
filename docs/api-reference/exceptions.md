# Exceptions

Namespace: `KernSmith`

KernSmith throws a small hierarchy of exceptions for generation errors. All derive from
`BmFontException`, so catching that one handles every KernSmith-specific failure.

```
Exception
└── BmFontException
    ├── FontParsingException
    ├── RasterizationException
    └── AtlasPackingException
```

```csharp
try
{
    var result = BmFont.Generate("font.ttf", new FontGeneratorOptions { Size = 32 });
}
catch (FontParsingException ex)
{
    // The font file is corrupt, unsupported, or missing the requested face/system font.
}
catch (BmFontException ex)
{
    // Any other KernSmith generation failure (rasterization, atlas packing).
}
```

> Argument validation (null arguments, out-of-range size, non-positive texture dimensions,
> invalid feature combinations) throws standard BCL exceptions such as
> `ArgumentNullException`, `ArgumentOutOfRangeException`, and `InvalidOperationException`
> rather than `BmFontException`.

## BmFontException

The base type for all KernSmith errors. Catch this to handle any generation failure.

| Member | Description |
|--------|-------------|
| `BmFontException(string message)` | Error with a message. |
| `BmFontException(string message, Exception inner)` | Error wrapping an underlying exception. |

## FontParsingException

Thrown when a font cannot be read. This is the most common KernSmith exception. It is raised when:

- font data is too small, has an invalid sfnt magic, or is missing a required table
  (`head`, `cmap`) during TTF/OTF parsing;
- a WOFF file is malformed, or the input is WOFF2 (which is not supported);
- a requested face index is out of range for a `.ttc`/`.otc` collection;
- a backend fails to initialize the font (e.g. FreeType or StbTrueType load failure);
- a requested system font family is not installed and is not registered.

| Member | Description |
|--------|-------------|
| `FontParsingException(string message)` | Error with a message. |
| `FontParsingException(string message, Exception inner)` | Error wrapping an underlying exception. |
| `FontParsingException(string tableTag, int offset, string details)` | Error at a specific table and byte offset. |
| `TableTag` | `string?` -- the font table that failed, if known (e.g. `"GPOS"`). |
| `Offset` | `int?` -- byte offset of the error, if known. |

## RasterizationException

Thrown when a glyph cannot be rendered from the font.

| Member | Description |
|--------|-------------|
| `RasterizationException(string message)` | Error with a message. |
| `RasterizationException(string message, Exception inner)` | Error wrapping an underlying exception. |
| `RasterizationException(int codepoint, string details)` | Error for a specific failing codepoint. |
| `Codepoint` | `int?` -- the Unicode codepoint that failed to render, if known. |

## AtlasPackingException

Thrown when glyphs do not fit into the texture atlas -- for example, when rendering into a
fixed [target region](options.md) too small for the glyphs. Try a larger max texture size.

| Member | Description |
|--------|-------------|
| `AtlasPackingException(string message)` | Error with a message. |
| `AtlasPackingException(string message, Exception inner)` | Error wrapping an underlying exception. |
| `AtlasPackingException(int glyphWidth, int glyphHeight, int maxTextureSize)` | Error for a single glyph too large for the texture. |
| `GlyphWidth` | `int?` -- width of the glyph that did not fit, if known. |
| `GlyphHeight` | `int?` -- height of the glyph that did not fit, if known. |
| `MaxTextureSize` | `int?` -- the max texture dimension that was exceeded, if known. |
