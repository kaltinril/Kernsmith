# KernSmith -- Extended Metadata Format

> How KernSmith stores metadata that doesn't fit in the standard BMFont .fnt format.
> Research-backed approach: follow Hiero's precedent of adding custom fields to the existing format.

---

## Problem

The BMFont .fnt format has a fixed schema designed in 2004. It has no fields for:
- SDF spread/distance range
- Gradient settings (angle, midpoint, colors)
- Shadow configuration
- Outline width used during generation
- Channel semantics beyond the standard encoding
- Super sampling level
- Variable font axis values used
- Generation tool and version

We need a way to store this metadata without breaking compatibility with existing BMFont readers.

---

## Research: How Other Tools Handle This

| Tool | Approach | Details |
|------|----------|---------|
| **Hiero (LibGDX)** | Custom field in .fnt `info` line | Adds `spread=4` to the standard info line. Existing readers ignore unknown keys. |
| **msdf-atlas-gen** | Custom JSON format | Uses its own JSON schema with `distanceRange` field. Not BMFont-compatible. |
| **msdf-bmfont-xml** | JSON output with extra fields | Adds `distanceRange` to JSON. Standard .fnt output has no SDF metadata. |
| **TextMeshPro (Unity)** | Proprietary binary format | Completely custom, not BMFont-compatible. |

**Conclusion**: Hiero's approach is the best — add custom key-value pairs to the existing .fnt format. All known BMFont parsers skip unknown keys, so compatibility is preserved.

---

## Our Approach: Inline Custom Fields

### Strategy

Add custom fields directly to the .fnt output in all three formats (text, XML, binary). Use a `KernSmith` tag/element/block for our extended metadata.

### Text Format

Add custom key-value pairs to existing lines where appropriate, plus a dedicated `KernSmith` line:

```
info face="Arial" size=64 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=1 aa=1 padding=4,4,4,4 spacing=1,1 outline=2 spread=4
common lineHeight=76 base=60 scaleW=512 scaleH=512 pages=1 packed=0 alphaChnl=0 redChnl=0 greenChnl=0 blueChnl=0
KernSmith version=1 generator="KernSmith/0.1.0" sdf=1 spread=4 gradient_angle=45 gradient_midpoint=0.3 gradient_start=FFD700 gradient_end=DC143C shadow_x=2 shadow_y=2 shadow_blur=4 shadow_color=000000 shadow_opacity=0.5 outline=2 supersampling=2
page id=0 file="Arial_0.png"
chars count=95
char id=65 ...
```

- `spread=N` on the `info` line — matches Hiero convention for maximum compatibility
- `KernSmith` tag line — contains all extended metadata in one place
- Existing BMFont readers ignore both the unknown `spread` key and the unknown `KernSmith` tag

### XML Format

Add a custom `<KernSmith>` element inside `<font>`:

```xml
<?xml version="1.0"?>
<font>
  <info face="Arial" size="64" spread="4" ... />
  <common ... />
  <KernSmith version="1" generator="KernSmith/0.1.0">
    <sdf spread="4" />
    <gradient angle="45" midpoint="0.3" startColor="FFD700" endColor="DC143C" />
    <shadow offsetX="2" offsetY="2" blur="4" color="000000" opacity="0.5" />
    <outline width="2" />
    <supersampling level="2" />
    <variableAxes wght="700" wdth="75" />
  </KernSmith>
  <pages>...</pages>
  <chars>...</chars>
</font>
```

### Binary Format

Add a new block type (type 6) after the standard 5 blocks:

```
Block type: 6
Content: UTF-8 JSON string (null-terminated)
```

The JSON contains the same data as the XML `<KernSmith>` element:

```json
{
  "version": 1,
  "generator": "KernSmith/0.1.0",
  "sdf": { "spread": 4 },
  "gradient": { "angle": 45, "midpoint": 0.3, "startColor": "FFD700", "endColor": "DC143C" },
  "shadow": { "offsetX": 2, "offsetY": 2, "blur": 4, "color": "000000", "opacity": 0.5 },
  "outline": { "width": 2 },
  "superSampling": 2,
  "variableAxes": { "wght": 700, "wdth": 75 }
}
```

Existing binary readers skip unknown block types (they read the block size and skip that many bytes), so this is safe.

---

## Extended Metadata Fields

| Field | Type | Where Used | Description |
|-------|------|-----------|-------------|
| `version` | int | KernSmith block | Metadata format version (currently 1) |
| `generator` | string | KernSmith block | Tool name and version |
| `sdf` | bool | info line + KernSmith | Whether SDF rendering was used |
| `spread` | int | info line + KernSmith | SDF spread/distance range in pixels |
| `gradient_angle` | float | KernSmith | Gradient direction in degrees |
| `gradient_midpoint` | float | KernSmith | Gradient bias (0.0-1.0) |
| `gradient_start` | hex RGB | KernSmith | Gradient start color |
| `gradient_end` | hex RGB | KernSmith | Gradient end color |
| `shadow_x` | int | KernSmith | Shadow X offset |
| `shadow_y` | int | KernSmith | Shadow Y offset |
| `shadow_blur` | int | KernSmith | Shadow blur radius |
| `shadow_color` | hex RGB | KernSmith | Shadow color |
| `shadow_opacity` | float | KernSmith | Shadow opacity (0.0-1.0) |
| `shadow_mode` | string | KernSmith | "drop" or "directional" |
| `shadow_angle` | float | KernSmith | Directional shadow angle |
| `shadow_length` | int | KernSmith | Directional shadow length |
| `shadow_fade` | bool | KernSmith | Directional shadow fade |
| `outline` | int | KernSmith | Outline width used |
| `supersampling` | int | KernSmith | Super sampling level |
| `variable_axes` | dict | KernSmith | Variable font axis values (tag=value pairs) |

---

## Implementation Changes

### Writing (output side)

1. Update `TextFormatter` to:
   - Add `spread=N` to the info line when SDF is enabled
   - Emit a `KernSmith` line when any extended metadata is present

2. Update `XmlFormatter` to:
   - Add `spread` attribute to `<info>` when SDF is enabled
   - Add `<KernSmith>` element when extended metadata is present

3. Update `BmFontBinaryFormatter` to:
   - Write block type 6 with JSON payload when extended metadata is present

4. Create `ExtendedMetadata` model class:
   ```csharp
   public sealed class ExtendedMetadata
   {
       public int? SdfSpread { get; init; }
       public GradientSettings? Gradient { get; init; }
       public ShadowSettings? Shadow { get; init; }
       public int? OutlineWidth { get; init; }
       public int? SuperSampling { get; init; }
       public IReadOnlyDictionary<string, float>? VariableAxes { get; init; }
   }
   ```

5. Add `ExtendedMetadata? Extended` property to `BmFontModel`

### Reading (input side)

6. Update `BmFontReader.ReadText` to:
   - Parse `spread=N` from the info line
   - Parse the `KernSmith` line if present

7. Update `BmFontReader.ReadXml` to parse `<KernSmith>` element

8. Update `BmFontReader.ReadBinary` to parse block type 6

### Pipeline integration

9. `BmFontModelBuilder` populates `ExtendedMetadata` from the generation options
10. Only written when at least one extended field is set — vanilla BMFont output is unchanged

---

## Compatibility

| Reader | Behavior with our extensions |
|--------|------------------------------|
| MonoGame.Extended BitmapFont | Ignores unknown `spread` key and `KernSmith` line — works fine |
| LibGDX BitmapFont | Ignores unknown tags — works fine. Reads `spread` if using DistanceFieldFont. |
| Godot BMFont | Ignores unknown lines — works fine |
| Cocos2d | Ignores unknown lines — works fine |
| Our BmFontReader | Reads and populates ExtendedMetadata |
| BMFont.exe (load) | Would ignore unknown lines — works fine |

**Zero compatibility risk.** All tested BMFont readers skip unknown content.

---

## Cross-References

- SDF rendering: [plan-rasterization.md](plan-rasterization.md)
- Shadow/gradient effects: [plan-bmfont-parity.md](plan-bmfont-parity.md) (items 16, gradient)
- Output formats: [plan-output-formats.md](plan-output-formats.md)
- BMFont reader: implemented in `Output/BmFontReader.cs`
- CLI config: [plan-cli.md](plan-cli.md)
