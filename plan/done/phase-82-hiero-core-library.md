# Phase 82 — Hiero Core Library Changes

> **Status**: Complete
> **Created**: 2026-03-22
> **Depends on**: Phase 81
> **Priority**: KernSmith ↔ .hiero round-trip fidelity (export our fonts to Hiero, read them back). Full Hiero ecosystem compatibility is deferred to Phase 100.
> **Goal**: Add Hiero `.hiero` config read/write support to the KernSmith NuGet library.

---

## Current Architecture

The config pipeline is currently hardcoded to `.bmfc`:

```
BmfcConfigReader.Read(path) → BmfcConfig → FontGeneratorOptions → BmFont.Generate()
BmFontResult.ToBmfc() → BmfcConfigWriter.Write() → string
```

Key files:
- `src/KernSmith/Config/BmfcConfig.cs` — Config model wrapping `FontGeneratorOptions`
- `src/KernSmith/Config/BmfcConfigReader.cs` — Static parser (~60+ key=value pairs)
- `src/KernSmith/Config/BmfcConfigWriter.cs` — Static serializer
- `src/KernSmith/Config/FontGeneratorOptions.cs` — 50+ properties for generation settings
- `src/KernSmith/BmFont.cs` — `FromConfig(string bmfcPath)`, `FromConfig(BmfcConfig config)`
- `src/KernSmith/BmFontBuilder.cs` — `FromConfig()` overloads
- `src/KernSmith/Output/BmFontResult.cs` — `ToBmfc()`, `ToFile()`

**No abstraction exists** for config formats — `BmfcConfigReader`/`BmfcConfigWriter` are static classes with no interface.

## Changes Required

### 1. Config Format Abstraction

Create interfaces so both formats can be dispatched generically:

**New file: `src/KernSmith/Config/IConfigReader.cs`**
```csharp
namespace KernSmith;

public interface IConfigReader
{
    BmfcConfig Read(string filePath);
    BmfcConfig Parse(string content);
}
```

**New file: `src/KernSmith/Config/IConfigWriter.cs`**
```csharp
namespace KernSmith;

public interface IConfigWriter
{
    string Write(BmfcConfig config);
    void WriteToFile(BmfcConfig config, string filePath);
}
```

> **Note:** `BmfcConfigReader`/`BmfcConfigWriter` are existing static classes and will NOT implement these interfaces in this phase. The interfaces define the contract for future non-static implementations. `ConfigFormatFactory` dispatches to static methods directly. The interfaces are provided for documentation and potential future refactoring.

**New file: `src/KernSmith/Config/ConfigFormatFactory.cs`**
```csharp
namespace KernSmith;

public static class ConfigFormatFactory
{
    public static BmfcConfig ReadConfig(string filePath)
    {
        // READ path is content-based: detect the format by inspecting the
        // file contents, using the extension only as a fallback tiebreaker
        // when content is inconclusive. Non-.hiero / inconclusive content is
        // parsed as BMFont — this is intentionally lenient and never throws.
        var format = ConfigFormatDetector.DetectFromContent(filePath);
        return format == ConfigFormat.Hiero
            ? HieroConfigReader.Read(filePath)
            : BmfcConfigReader.Read(filePath);
    }

    public static void WriteConfig(BmfcConfig config, string filePath)
    {
        // WRITE path is by extension: .hiero -> Hiero, anything else -> BMFont.
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".hiero")
            HieroConfigWriter.WriteToFile(config, filePath);
        else
            BmfcConfigWriter.WriteToFile(config, filePath);
    }
}
```

> **Public API surface that Phases 83 & 84 depend on (must be public + tested before those phases start):**
> - `static ConfigFormatFactory.ReadConfig(string filePath) → BmfcConfig` — auto-detects format by inspecting file **content** (extension is only a fallback tiebreaker when content is inconclusive); non-`.hiero`/inconclusive content is parsed as BMFont. Intentionally lenient — does **not** throw on an unknown/missing extension.
> - `static ConfigFormatFactory.WriteConfig(BmfcConfig config, string filePath) → void` — selects format by file extension (`.hiero` → Hiero, anything else → BMFont). Does **not** throw on an unknown/unsupported extension.
> - `static HieroConfigReader.Read(string filePath) → BmfcConfig` and `HieroConfigReader.Parse(string content) → BmfcConfig` — mirroring the existing static `BmfcConfigReader`.
> - `static HieroConfigWriter.Write(BmfcConfig config) → string` and `HieroConfigWriter.WriteToFile(BmfcConfig config, string filePath) → void` — mirroring the existing static `BmfcConfigWriter`.

### 2. Hiero Config Reader

**New file: `src/KernSmith/Config/HieroConfigReader.cs`**

Responsibilities:
- Provide both `Read(string filePath)` and `Parse(string content)` methods (matching `BmfcConfigReader` API)
- Parse `key=value` lines (split on first `=`)
- Map font properties: `font.name`, `font.size`, `font.bold`, `font.italic`, `font.gamma`, `font.mono`
- Map font file: `font2.file` + `font2.use`
- Map padding: `pad.top/right/bottom/left`
- Drop `pad.advance.x/y` with warning (per-glyph advance adjustment has no KernSmith equivalent; deferred to Phase 100)
- Map texture: `glyph.page.width/height`
- Parse `glyph.text` with `\n` unescaping → build CharacterSet from literal characters
- Parse effect blocks: track current effect class, accumulate properties, map to KernSmith effects
- Handle `render_type` (log warning if not FreeType)
- Resolve relative font file paths against config file directory

Effect mapping logic:
- `ColorEffect` → ignore on import (log warning if non-white); always write white on export. Full fill-color support deferred to Phase 100.
- `GradientEffect` → `GradientStartR/G/B`, `GradientEndR/G/B`; Hiero's Offset/Scale/Cyclic have no direct equivalent (KernSmith uses `GradientAngle`, `GradientMidpoint`)
- `OutlineEffect` → `Outline` (float→int via `Math.Round()`, matching the existing `BmfcConfigWriter` rounding pattern), `OutlineR`, `OutlineG`, `OutlineB`; join has no KernSmith equivalent (dropped)
- `ShadowEffect` → `ShadowOffsetX/Y`, `ShadowR/G/B`, `ShadowOpacity`, `ShadowBlur`. On export, the `HardShadow` flag controls blur output: if `HardShadow == true` write blur `0` (and log a warning), otherwise write `ShadowBlur` as-is. Hiero's two-parameter blur (kernel size + passes) is collapsed/deferred to Phase 100.
- `DistanceFieldEffect` → `Sdf = true`; Hiero's Scale and Spread have no KernSmith equivalent
- `OutlineWobbleEffect` / `OutlineZigzagEffect` → log warning, skip (no KernSmith equivalent)

### 3. Hiero Config Writer

**New file: `src/KernSmith/Config/HieroConfigWriter.cs`**

Responsibilities:
- Serialize `BmfcConfig` → `.hiero` key=value format
- Write font properties section
- Write font2 section (if font file is set)
- Write padding section
- Write glyph settings (texture size, character text with `\n` escaping)
- Write `render_type=2` (always FreeType)
- Write effect blocks based on active KernSmith effects, using the **effect-activation rules** below.
- **Canonical effect serialization order** (for stable round-trip): `ColorEffect`, `OutlineEffect`, `GradientEffect`, `ShadowEffect`, `DistanceFieldEffect`. Always emit effects in this fixed order regardless of any input ordering.
  - Always write `ColorEffect` (white default)
  - Write `OutlineEffect` if **Outline thickness > 0**. Hiero `Width` is a float; KernSmith `Outline` is an int — write it directly as a float-formatted value (and on import use `Math.Round()` for the float→int conversion, matching the existing `BmfcConfigWriter` rounding pattern).
  - Write `GradientEffect` if **gradient start/end colors are set**
  - Write `ShadowEffect` if **`ShadowOffsetX != 0` OR `ShadowOffsetY != 0` OR `ShadowBlur > 0`**
  - Write `DistanceFieldEffect` if **`Sdf == true`**
- **Shadow blur conversion**: KernSmith has `ShadowBlur` (a radius) plus a `HardShadow` flag. On export: if `HardShadow == true`, write blur `0`; otherwise write `ShadowBlur` as-is. Log a warning when `HardShadow == true` (the flag itself has no Hiero equivalent).
- Log warnings for KernSmith features with no Hiero equivalent (including `HardShadow`)

### 4. Public API Updates

**`src/KernSmith/BmFont.cs`** — Update `FromConfig(string)`:
```csharp
// Current: hardcoded to BmfcConfigReader
public static BmFontResult FromConfig(string bmfcPath)
{
    var config = BmfcConfigReader.Read(bmfcPath);
    return FromConfig(config);
}

// Updated: auto-detect format
public static BmFontResult FromConfig(string configPath)
{
    var config = ConfigFormatFactory.ReadConfig(configPath);
    return FromConfig(config);
}
```

**`src/KernSmith/BmFontBuilder.cs`** — Same pattern for `FromConfig(string)`.

**`src/KernSmith/Output/BmFontResult.cs`** — Add Hiero export:
```csharp
// Existing
public string ToBmfc() { ... }

// New
public string ToHiero() { ... }
```

**`ToHiero()` contract:** `public string ToHiero()` returns the `.hiero` file content as a string — the same contract as the existing `ToBmfc()`. It requires only `SourceOptions` to be set on the `BmFontResult` (identical preconditions to `ToBmfc()`) and throws `InvalidOperationException` when `SourceOptions` is null. `SourceFontFile` and `SourceFontName` are optional and serialize as empty strings when null.

**`src/KernSmith/Output/FileWriter.cs`** — No new config-path parameter:
- Config export is the **caller's** responsibility via `BmFontResult.ToBmfc()` / `BmFontResult.ToHiero()` (or `ConfigFormatFactory.WriteConfig()`), NOT a parameter on `FileWriter.Write()`.
- Do **not** add a config path parameter to `FileWriter.Write()`. Callers that want to persist a config string write it themselves using the appropriate `To*()` method.

### 5. Backward Compatibility

- `BmfcConfigReader` / `BmfcConfigWriter` remain unchanged as static classes
- `FromConfig(string)` continues to work with `.bmfc` paths (auto-detected)
- `ToBmfc()` remains available (not deprecated)
- No breaking changes to existing API surface

## Files Changed

| File | Change Type |
|------|-------------|
| `src/KernSmith/Config/IConfigReader.cs` | **NEW** |
| `src/KernSmith/Config/IConfigWriter.cs` | **NEW** |
| `src/KernSmith/Config/ConfigFormatFactory.cs` | **NEW** |
| `src/KernSmith/Config/HieroConfigReader.cs` | **NEW** |
| `src/KernSmith/Config/HieroConfigWriter.cs` | **NEW** |
| `src/KernSmith/BmFont.cs` | Modified — `FromConfig(string)` uses factory |
| `src/KernSmith/BmFontBuilder.cs` | Modified — `FromConfig(string)` uses factory |
| `src/KernSmith/Output/BmFontResult.cs` | Modified — add `ToHiero()` |
| `src/KernSmith/Output/FileWriter.cs` | **No change** — config export stays at caller level via `BmFontResult.ToBmfc()`/`ToHiero()` |

## Test Plan

| Test | Type | Description |
|------|------|-------------|
| Parse minimal .hiero | Unit | Font name + size only |
| Parse all font properties | Unit | Bold, italic, gamma, mono, font2 |
| Parse padding properties | Unit | All 6 padding/advance keys |
| Parse glyph settings | Unit | Page size, native rendering, text with `\n` |
| Parse effects: Color | Unit | Single color effect |
| Parse effects: Outline | Unit | Color, width, join |
| Parse effects: Shadow | Unit | All shadow properties |
| Parse effects: Gradient | Unit | All gradient properties |
| Parse effects: DistanceField | Unit | Color, scale, spread |
| Parse effects: Multiple | Unit | Stacked effects in order |
| Parse effects: Unknown | Unit | Graceful skip with warning |
| Write minimal .hiero | Unit | Round-trip minimal config |
| Write all properties | Unit | Full config round-trip |
| Write effects | Unit | All effect types serialize correctly |
| Character set: ASCII | Unit | Literal text → CharacterSet → literal text |
| Character set: Unicode | Unit | Non-ASCII characters preserved |
| Character set: Empty | Unit | Empty glyph.text handled |
| Format detection: .hiero | Integration | `BmFont.FromConfig("test.hiero")` works |
| Format detection: .bmfc | Integration | `BmFont.FromConfig("test.bmfc")` still works |
| Format detection: unknown | Integration | Unknown/inconclusive content falls back to BMFont (no exception) |
| Generate from .hiero | Integration | Full pipeline produces valid .fnt + .png |
| `ToHiero()` export | Integration | Result exports valid .hiero string |
| Lossy round-trip warning | Unit | KernSmith-only features trigger warnings |

## Estimated Complexity

- **New code**: ~500–700 lines across reader, writer, factory, interfaces
- **Modified code**: ~30 lines across BmFont.cs, BmFontBuilder.cs, BmFontResult.cs
- **Tests**: ~400–500 lines
- **Risk**: Low — additive changes only, no breaking API changes

## Review Decisions (2026-03-22)

Decisions made during pre-implementation review:

1. **`pad.advance.x/y`** — Drop on import with warning. Semantically different from `Spacing` (per-glyph advance vs atlas spacing). Deferred to Phase 100.
2. **ColorEffect** — Ignore on import (log warning if non-white), always write white on export. Full fill-color support deferred to Phase 100.
3. **Outline thickness** — Hiero's float width maps to `FontGeneratorOptions.Outline` (int). Use `Math.Round()` (matching the existing `BmfcConfigWriter` rounding pattern), not truncation, for float→int conversion.
4. **Shadow blur** — KernSmith `ShadowBlur` (radius) + `HardShadow` flag. On export: `HardShadow == true` → write blur `0` (log warning); else write `ShadowBlur` as-is. Hiero's two-parameter blur (kernel size + passes) deferred to Phase 100.
5. **Font paths on export** — Use relative paths, matching `BmfcConfigWriter` behavior.
6. **Gradient Offset/Scale/Cyclic, DistanceField Scale/Spread, Wobble/Zigzag** — All deferred to Phase 100.
7. **Scope focus** — Priority is KernSmith ↔ .hiero round-trip fidelity, not full Hiero ecosystem compatibility.

---

> **Plan review 2026-03-24**: Fixed `OutlineThickness` references to `Outline` (the actual `FontGeneratorOptions` property name). Added `HardShadow` to shadow effect mapping notes (added in Phase 77B). Clarified outline thickness mapping to `FontGeneratorOptions.Outline`.
