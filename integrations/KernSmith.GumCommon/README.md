# KernSmith.GumCommon

Shared integration logic that bridges KernSmith bitmap font generation with Gum's BmfcSave font descriptor.

## Overview

This package provides the common mapping layer used by all platform-specific Gum integration packages (`KernSmith.MonoGameGum`, `KernSmith.FnaGum`, `KernSmith.KniGum`). It translates Gum's `BmfcSave` font configuration into KernSmith's `FontGeneratorOptions` and drives the font generation pipeline.

By isolating the shared logic here, each platform package only needs to handle framework-specific concerns like texture creation.

**Targets**: `net8.0`, `net10.0`

## Build

```
dotnet build integrations/KernSmith.GumCommon/KernSmith.GumCommon.csproj
```

See the [root README](../../README.md) for full project documentation.
