# KernSmith

The core library for generating BMFont-compatible bitmap fonts from TTF/OTF/WOFF files.

## Overview

KernSmith combines FreeTypeSharp for glyph rasterization with custom TTF table parsers for GPOS kerning extraction. It packs glyphs into texture atlases and outputs BMFont `.fnt` + `.png`/`.tga`/`.dds` pairs.

Key features include layered effects (outline, gradient, shadow), color font support (COLRv0/CPAL, sbix, CBDT), variable fonts, SDF rendering, font subsetting, channel packing, super sampling, and extended metadata. The pipeline runs entirely in memory by default.

## Install

```
dotnet add package KernSmith
```

## Usage

```csharp
using KernSmith;

var result = BmFont.Generate("path/to/font.ttf", new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii
});
// result.Model contains the .fnt descriptor
// result.Pages contains the texture atlas pages
```

## Native AOT and trimming

KernSmith normally auto-discovers a rasterizer backend, but this uses reflection and **does not work under Native AOT or trimming** -- generation fails with a "backend is not registered" error unless you register a backend explicitly. Use **StbTrueType** (`KernSmith.Rasterizers.StbTrueType`), the only AOT-compatible backend (pure C#, no native dependencies):

```csharp
using System.Runtime.CompilerServices;
using KernSmith.Rasterizers.StbTrueType;

// Force the backend to load and register; the typeof reference prevents trimming.
RuntimeHelpers.RunClassConstructor(typeof(StbTrueTypeRasterizer).TypeHandle);
```

See the [root README](../../README.md) for full project documentation.
