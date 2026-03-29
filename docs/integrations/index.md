# Integrations

KernSmith provides integration packages for the [Gum](https://docs.flatredball.com/gum) UI framework, generating `BitmapFont` instances entirely in memory with no disk I/O.

## Packages

| Package | Framework | Status |
|---------|-----------|--------|
| [MonoGameGum](monogamegum.md) | MonoGame + Gum | Available |
| [KniGum](knigum.md) | KNI + Gum | Available |
| [FnaGum](fnagum.md) | FNA + Gum | Planned |
| [GumCommon](gumcommon.md) | Shared (no framework dependency) | Available |

All framework packages depend on `KernSmith.GumCommon` for shared generation logic.

## Architecture

```
KernSmith.MonoGameGum ─┐
KernSmith.FnaGum ──────┤──> KernSmith.GumCommon ──> KernSmith (core)
KernSmith.KniGum ──────┘
```

For initial Gum project setup, see the [Gum documentation](https://docs.flatredball.com/gum).
