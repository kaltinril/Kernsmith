# Phase 81 — Hiero .hiero Configuration Format Support

> **Status**: Planning
> **Created**: 2026-03-22
> **Goal**: Add read/write support for the libGDX Hiero `.hiero` configuration file format as an alternative to BMFont `.bmfc`.

---

## Background

KernSmith currently supports only the BMFont `.bmfc` configuration format for saving and loading font generation settings. The Hiero tool (part of libGDX) uses its own `.hiero` format — a plain-text key=value file that is popular in the libGDX/Java game development ecosystem.

Adding `.hiero` support means KernSmith users can:
- Import existing Hiero configurations and generate fonts without re-configuring
- Export configurations for use with Hiero or other libGDX-compatible tools
- Choose their preferred config format for project files

## Format Reference

> See [REF-10: Hiero Configuration File Format Reference](../reference/REF-10-hiero-format-reference.md) for the complete format specification, property tables, effects system, value serialization, example files, and property mapping to KernSmith types.

## Format Summary

The `.hiero` file is a **plain-text key=value format**:
- One property per line: `key=value`
- No sections, headers, or quoting
- UTF-8 encoded
- Effects serialized as repeating `effect.class` / `effect.<ValueName>` groups
- Characters specified as literal text in `glyph.text` (vs numeric codepoint ranges in .bmfc)

## Key Differences from .bmfc

| Aspect | .hiero | .bmfc |
|--------|--------|-------|
| Character specification | Literal text string | Numeric codepoint ranges |
| Effects | Java class-based repeating blocks | Fixed config sections |
| Font source | System name OR file path (separate keys) | Font descriptor string |
| Encoding | UTF-8 with escaped `\n` | ASCII with codepoint numbers |

## Property Mapping Strategy

The `.hiero` format maps to KernSmith's existing `FontGeneratorOptions` / `BmfcConfig` model:

**Direct mappings**: `font.size` → `Size`, `font.bold` → `Bold`, `font.italic` → `Italic`, texture dimensions, characters (`pad.advance.x/y` → requires careful mapping (Hiero adjusts advance width, not atlas spacing))
**Inverted mappings**: `font.mono=true` → `AntiAlias=AntiAliasMode.None`
**Effect mappings**: Outline, Gradient, Shadow, DistanceField effects map to existing KernSmith effect properties
**Unmapped Hiero → KernSmith**: `render_type` (always FreeType), `font.gamma`, `OutlineWobbleEffect`, `OutlineZigzagEffect`
**Unmapped KernSmith → Hiero** (non-exhaustive list): `SuperSampleLevel`, `ChannelPacking`, `ColorFont`, `VariationAxes`, `PowerOfTwo`, `AutofitTexture`, `HeightPercent`, `EqualizeCellHeights`, `ForceOffsetsToZero`, `EnableHinting`, `Dpi`, `FaceIndex`, `CustomGlyphs`, `Kerning` (toggle), `CollectMetrics`, `TextureFormat` (TGA/DDS), `PackingAlgorithm`, `HardShadow`

## Implementation Phases

This work is broken across phases 81–85:

| Phase | Scope | Description |
|-------|-------|-------------|
| **81** | Format reference | This document + REF-10 |
| **82** | Core library | `HieroConfigReader`, `HieroConfigWriter`, config abstraction layer, public API updates |
| **83** | UI changes | File dialogs, project service, drag-drop, format indicator |
| **84** | CLI changes | `--config` auto-detection, `init` format flag, batch mixed-format support |
| **85** | Documentation | README, CLI docs, samples, NuGet metadata, CHANGELOG |

## Design Decisions

### Reuse `BmfcConfig` as Intermediate Model
Rather than creating a separate `HieroConfig` model, the Hiero reader/writer will map to/from the existing `BmfcConfig` (or a renamed `FontProjectConfig`). This avoids duplicating the entire config pipeline.

### Extension-Based Format Detection
Auto-detect format from file extension (`.hiero` vs `.bmfc`) rather than requiring explicit format flags. This is the simplest UX and matches the pattern already used for `.fnt` output format detection in `ConvertCommand`.

### Lossy Round-Trip Is Acceptable
Since the formats don't have 1:1 feature parity, round-tripping `.hiero` → KernSmith → `.hiero` may lose KernSmith-specific settings (and vice versa). This is documented and expected. A warning should be shown when saving to a format that will lose settings.

### Effects Mapping
- Hiero's `OutlineWobbleEffect` and `OutlineZigzagEffect` have no KernSmith equivalent — they are silently dropped on import (with a warning logged)
- KernSmith's advanced features (channel packing, variable fonts, supersampling, etc.) have no Hiero equivalent — they are omitted on export (with a warning logged)

## Risks & Open Questions

1. **Character encoding edge cases** — Hiero uses literal text with `\n` escaping; need to handle Unicode properly
2. **Effect stacking order** — Hiero applies effects in list order; need to verify KernSmith matches
3. **Padding semantics** — Hiero's `pad.advance.x/y` maps to spacing, not padding; verify sign conventions
4. **Font resolution** — Hiero's `font.name` is a Java font family name; system font lookup may differ across platforms

## Test Strategy

- Unit tests: Round-trip parse/write for all property types
- Unit tests: Effect serialization/deserialization for all 7 effect types
- Integration tests: Load real `.hiero` files from libGDX examples
- Integration tests: Generate fonts from `.hiero` config and compare output
- Edge cases: Empty glyph text, Unicode characters, missing properties, unknown effects

---

> **Plan review 2026-03-24**: Added `HardShadow` to unmapped KernSmith-to-Hiero properties list (added in Phase 77B).
