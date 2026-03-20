# bmfontier -- Bug Fixes and Audit Findings

> Captures all findings from the codebase audit: missing BMFC config keys,
> GenerateCommand issues, and utility command defects.
>
> **Date**: 2026-03-19

---

## Summary

| Priority | Count | Description |
|----------|-------|-------------|
| Critical | 1 | Config merge silently drops CLI fields |
| High | 7 | Dead code, argument parsing hazards, config round-trip data loss, core pipeline bugs |
| Medium | 11 | Exception handling gaps, inconsistent parsing, global state issues, core pipeline gaps |
| Low | 6 | Minor inconsistencies and wasteful operations |

---

## Critical

| # | Issue | Description | Affected Files | Status |
|---|-------|-------------|----------------|--------|
| GC-1 | Config merge drops CLI fields | When `--config` is used with CLI flags, `MergeConfigIntoOptions` omits OutputFormat, AntiAlias, MaxTextureSize, Outline, Dpi, FaceIndex, PackingAlgorithm, CharsetPreset, InstanceName. These CLI values are silently lost. | `GenerateCommand.cs` | Not started |

---

## High

| # | Issue | Description | Affected Files | Status |
|---|-------|-------------|----------------|--------|
| BF-1 | BMFC parser/writer missing 14 config keys | The parser and writer only handle 22 keys. 14 `CliOptions` properties have CLI flags but no `.bmfc` support -- they are lost in a save/load round-trip. See table below. | `BmfcParser.cs`, `BmfcWriter.cs` | Not started |
| GC-2 | `--instance` is dead code | Parsed into `CliOptions.InstanceName` but never wired to `FontGeneratorOptions` (which has no `InstanceName` property). Silently does nothing. | `GenerateCommand.cs`, `CliOptions.cs`, `FontGeneratorOptions.cs` | Not started |
| GC-3 | `--gradient` two-arg form can eat the next flag | `--gradient FF0000 --outline 3` would set `GradientBottom` to `--outline`. Confusing error later in `ColorParser`. | `GenerateCommand.cs` | Not started |
| GC-5 | `--system-font` + `--config` with FontPath can fail | File existence check validates config's `FontPath` even when user intended system font. | `GenerateCommand.cs` | Not started |

### BF-1 Detail -- Missing BMFC Config Keys

| Key (suggested) | Section | CliOptions Property | CLI Flag |
|---|---|---|---|
| `super-sample` | `[rendering]` | `SuperSampleLevel` | `--super-sample` |
| `fallback-char` | `[rendering]` | `FallbackCharacter` | `--fallback-char` |
| `hinting` | `[rendering]` | `EnableHinting` | `--hinting`/`--no-hinting` |
| `height-percent` | `[rendering]` | `HeightPercent` | `--height-percent` |
| `match-char-height` | `[rendering]` | `MatchCharHeight` | `--match-char-height` |
| `color-font` | `[rendering]` | `ColorFont` | `--color-font` |
| `color-palette` | `[rendering]` | `ColorPaletteIndex` | `--color-palette` |
| `max-texture-width` | `[atlas]` | `MaxTextureWidth` | `--max-texture-width` |
| `max-texture-height` | `[atlas]` | `MaxTextureHeight` | `--max-texture-height` |
| `autofit` | `[atlas]` | `AutofitTexture` | `--autofit` |
| `texture-format` | `[atlas]` | `TextureFormat` | `--texture-format` |
| `equalize-heights` | `[atlas]` | `EqualizeCellHeights` | `--equalize-heights` |
| `force-offsets-zero` | `[atlas]` | `ForceOffsetsToZero` | `--force-offsets-zero` |
| `instance` | `[variable]` | `InstanceName` | `--instance` |

---

## Medium

| # | Issue | Description | Affected Files | Status |
|---|-------|-------------|----------------|--------|
| GC-4 | Outline set in two places | Set in `FontGeneratorOptions.Outline` AND as a manually-added `OutlinePostProcessor`. Fragile but not currently broken. | `GenerateCommand.cs` | Not started |
| GC-6 | Shadow post-processor not exposed via CLI | No `--shadow` flag exists. | `GenerateCommand.cs` | Not started |
| UC-1 | ConsoleOutput global state never reset | `_verbose`, `_quiet`, `_noColor` are static and write-only-to-true. | `ConsoleOutput.cs` | Not started |
| UC-3 | ColorParser rejects 4-char shorthand RGBA | `#F00A` rejected but 8-char `#FF0000AA` accepted. Inconsistent. | `ColorParser.cs` | Not started |
| UC-4 | InfoCommand FileNotFoundException excluded from catch but never caught | Unhandled exception with raw stack trace. | `InfoCommand.cs` | Not started |
| UC-5 | InspectCommand missing IOException handler | Unlike `ConvertCommand` which catches it. | `InspectCommand.cs` | Not started |
| UC-6 | ListFontsCommand no exception handling for font scanning | `UnauthorizedAccessException` or `DirectoryNotFoundException` not caught. | `ListFontsCommand.cs` | Not started |
| UC-7 | ListFontsCommand positional args silently ignored | `list-fonts roboto` silently drops `roboto`, should treat as filter or error. | `ListFontsCommand.cs` | Not started |
| UC-8 | ConvertCommand page file copy doesn't handle subdirectory paths | Nested page paths fail, absolute paths bypass output directory. | `ConvertCommand.cs` | Not started |

---

## Low

| # | Issue | Description | Affected Files | Status |
|---|-------|-------------|----------------|--------|
| GC-7 | Boolean config merge is one-directional | Cannot un-set config-file booleans from CLI (e.g., can't disable bold that config enables). | `GenerateCommand.cs` | Not started |
| UC-2 | `--verbose` and `--quiet` accepted simultaneously | Contradictory behavior, undocumented. | `ConsoleOutput.cs` | Not started |
| UC-9 | InspectCommand DetectFormat decodes entire file as UTF-8 | Wasteful, only needs first few bytes. | `InspectCommand.cs` | Not started |
| UC-10 | InspectCommand DetectFormat has no validation for "Text" fallback | Non-BMFont files reported as "Text". | `InspectCommand.cs` | Not started |

---

## Implementation Order

1. **GC-1** (Critical) -- fix config merge to preserve all CLI overrides
2. **BF-1** (High) -- add missing 14 keys to BMFC parser and writer
3. **GC-2** (High) -- wire `--instance` through to `FontGeneratorOptions` or remove flag
4. **GC-3** (High) -- validate gradient arguments to prevent flag consumption
5. **GC-5** (High) -- skip file existence check when `--system-font` is set
6. **Medium issues** -- address in any order; group related command fixes together
7. **Low issues** -- address opportunistically

---

## Risks

| Risk | Mitigation |
|------|------------|
| GC-1 fix may change behavior for users relying on current merge order | Document merge semantics: CLI flags always win over config file |
| BF-1 adds new config keys that older parsers won't recognize | Keys are additive; unknown keys are already ignored on read |
| Fixing GC-3 gradient parsing may break scripts using two-arg form | Keep two-arg form but validate second arg is not a flag prefix |

---

## Core Library Pipeline Issues

| # | Priority | Issue | Description | Affected Files | Status |
|---|----------|-------|-------------|----------------|--------|
| CL-1 | High | Outline only works inside ChannelCompositor path | Setting `WithOutline(N)` without a `ChannelConfig` does nothing — the `OutlinePostProcessor` is only added inside the channel compositing branch. Fix: auto-add `OutlinePostProcessor` when `options.Outline > 0` and no `ChannelConfig`. | `BmFont.cs` | Not started |
| CL-2 | High | No guard against SuperSampleLevel > 1 + Sdf = true | Box filter corrupts SDF distance values. Fix: throw `InvalidOperationException` when both are set. | `BmFont.cs` | Not started |
| CL-3 | High | ChannelCompositor clips outline fringe | Iterates base glyph dimensions, not expanded outline glyph dimensions. Outline fringe outside the base glyph rect is never rendered. Fix: use outline glyph dimensions for pack rects and blit loop. | `ChannelCompositor.cs` | Not started |
| CL-4 | Medium | PowerOfTwo option declared but never checked | Atlas sizing always uses `NextPowerOfTwo`. Fix: check the option in the sizing path. | `BmFont.cs` | Not started |
| CL-5 | Medium | FallbackCharacter declared but never used | Set in options but never added to the character set or recorded in output model. Fix: add to character set and record in output model. | `BmFont.cs`, `BmFontModelBuilder.cs` | Not started |
| CL-6 | Low | MatchCharHeight uses integer division | `rasterOptions.Size * rasterOptions.Size / maxRenderedHeight` is integer math, loses precision. Fix: use `Math.Round` with double cast. | `BmFont.cs` | Not started |
| CL-7 | Low | ChannelPackedAtlasBuilder assumes grayscale, no guard for RGBA | Channel packing reads a single byte per pixel but RGBA glyphs have 4 bytes. Fix: check `glyph.Format` and skip or throw. | `ChannelPackedAtlasBuilder.cs` | Not started |

---

## Estimated Effort

- **Total**: 3-4 days focused work
- **Risk**: Low-Medium -- most fixes are localized to individual commands
- **Needs**: Existing test suite covers core generation; CLI command tests may need expansion
