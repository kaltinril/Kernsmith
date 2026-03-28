# KernSmith.KniGum

Runtime bitmap font generation for KNI + Gum games using KernSmith.

## Overview

This package generates `BitmapFont` instances entirely in memory for KNI (Kni is Not XNA) projects that use the Gum UI framework. No disk I/O is required -- fonts are rasterized, packed, and loaded into GPU textures at runtime.

It shares the same `KernSmithFontCreator` implementation as the MonoGame integration (via linked source file), adapted to the KNI framework (`nkast.Xna.Framework`) and Gum.KNI package dependencies.

### Quick Setup

```
dotnet add package KernSmith.KniGum
```

```csharp
CustomSetPropertyOnRenderable.InMemoryFontCreator = new KernSmithFontCreator(GraphicsDevice);
```

**Target**: `net8.0`

## Build

```
dotnet build integrations/KernSmith.KniGum/KernSmith.KniGum.csproj
```

See the [root README](../../README.md) for full project documentation.
