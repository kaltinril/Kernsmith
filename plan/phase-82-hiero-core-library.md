# Phase 82 — Hiero Core Library Changes

> **Status**: Planning
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
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".hiero" => HieroConfigReader.Read(filePath),
            ".bmfc" => BmfcConfigReader.Read(filePath),
            _ => throw new BmFontException($"Unsupported config format: {ext}")
        };
    }

    public static void WriteConfig(BmfcConfig config, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".hiero": HieroConfigWriter.WriteToFile(config, filePath); break;
            case ".bmfc": BmfcConfigWriter.WriteToFile(config, filePath); break;
            default: throw new BmFontException($"Unsupported config format: {ext}");
        }
    }
}
```

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
- `OutlineEffect` → `OutlineThickness` (float→int, round-to-nearest), `OutlineR`, `OutlineG`, `OutlineB`; join has no KernSmith equivalent (dropped)
- `ShadowEffect` → `ShadowOffsetX/Y`, `ShadowR/G/B`, `ShadowOpacity`; Hiero's two-param blur collapses as `ShadowBlur = kernelSize * passes`. Two-param blur deferred to Phase 100.
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
- Write effect blocks based on active KernSmith effects:
  - Always write `ColorEffect` (white default)
  - Write `OutlineEffect` if outline thickness > 0
  - Write `GradientEffect` if gradient is enabled
  - Write `ShadowEffect` if shadow is enabled
  - Write `DistanceFieldEffect` if SDF is enabled
- Log warnings for KernSmith features with no Hiero equivalent

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

**`src/KernSmith/Output/FileWriter.cs`** — Update `ToFile()` to optionally write `.hiero` alongside output:
- Currently writes `.bmfc` alongside `.fnt` + images
- Should use `ConfigFormatFactory.WriteConfig()` if a config path is provided

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
| `src/KernSmith/Output/FileWriter.cs` | Modified — config format awareness |

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
| Format detection: unknown | Integration | Throws `BmFontException` |
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
3. **Outline thickness** — Use round-to-nearest (not truncation) for float→int conversion.
4. **Shadow blur** — Collapse as `ShadowBlur = kernelSize * passes`. Two-parameter blur deferred to Phase 100.
5. **Font paths on export** — Use relative paths, matching `BmfcConfigWriter` behavior.
6. **Gradient Offset/Scale/Cyclic, DistanceField Scale/Spread, Wobble/Zigzag** — All deferred to Phase 100.
7. **Scope focus** — Priority is KernSmith ↔ .hiero round-trip fidelity, not full Hiero ecosystem compatibility.
