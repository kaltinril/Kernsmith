# KernSmith.FnaGum

Runtime bitmap font generation for FNA + Gum games using KernSmith.

## Overview

This package generates `BitmapFont` instances entirely in memory for FNA projects that use the Gum UI framework. No disk I/O is required -- fonts are rasterized, packed, and loaded into GPU textures at runtime.

It shares the same `KernSmithFontCreator` implementation as the MonoGame integration (via linked source file), adapted to the FNA and Gum.FNA package dependencies.

### Quick Setup

```
dotnet add package KernSmith.FnaGum
```

```csharp
CustomSetPropertyOnRenderable.InMemoryFontCreator = new KernSmithFontCreator(GraphicsDevice);
```

**Target**: `net8.0`

## Build

```
dotnet build integrations/KernSmith.FnaGum/KernSmith.FnaGum.csproj
```

See the [root README](../../README.md) for full project documentation.
