# REF-10: Hiero Configuration File Format Reference

## Overview

Hiero is a cross-platform bitmap font generation tool that produces Angel Code BMFont-compatible output (.fnt + .png files). Originally created by Kevin Glass (January 2007) as part of the Slick game framework, it was later ported to libGDX and is now maintained as part of that project under `extensions/gdx-tools`.

- **Author**: Kevin Glass (original), Nathan Sweet (libGDX port)
- **Language**: Java (Swing UI, LWJGL 2 backend)
- **Package**: `com.badlogic.gdx.tools.hiero`
- **Source**: `https://github.com/libgdx/libgdx/tree/master/extensions/gdx-tools/src/com/badlogic/gdx/tools/hiero`
- **Wiki**: `https://libgdx.com/wiki/tools/hiero`

## File Format Syntax

The `.hiero` file is a **plain-text key=value format** (not INI, JSON, or XML):

- UTF-8 encoding
- File extension: `.hiero`
- One property per line: `key=value`
- No quoting of values
- Blank lines used as visual separators (ignored during parsing)
- Lines split on first `=` only (`line.split("=", 2)`)
- Leading/trailing whitespace trimmed from key but NOT from value
- Newlines in `glyph.text` escaped as literal `\n` strings

---

## Complete Property Reference

### Font Properties

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `font.name` | String | `"Arial"` | System font family name |
| `font.size` | int | `12` (UI: `32`) | Font size in points |
| `font.bold` | boolean | `false` | Bold style flag |
| `font.italic` | boolean | `false` | Italic style flag |
| `font.gamma` | float | `0` (UI: `1.8`) | Gamma correction for FreeType rendering |
| `font.mono` | boolean | `false` | Monochrome (no antialiasing) mode for FreeType |

### Secondary Font (File-based)

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `font2.file` | String | `""` | Path to a TTF/OTF font file on disk |
| `font2.use` | boolean | `false` | Whether to use the file font instead of system font |

### Padding

| Key | Type | Default (code / UI) | Description |
|-----|------|---------------------|-------------|
| `pad.top` | int | `0` / `1` | Top padding in pixels |
| `pad.right` | int | `0` / `1` | Right padding in pixels |
| `pad.bottom` | int | `0` / `1` | Bottom padding in pixels |
| `pad.left` | int | `0` / `1` | Left padding in pixels |
| `pad.advance.x` | int | `0` / `-2` | X advance adjustment (added to xadvance) |
| `pad.advance.y` | int | `0` / `-2` | Y advance adjustment (added to line height) |

### Glyph / Texture Page Settings

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `glyph.page.width` | int | `512` | Texture atlas width. Valid: 32, 64, 128, 256, 512, 1024, 2048 |
| `glyph.page.height` | int | `512` | Texture atlas height. Same valid values |
| `glyph.native.rendering` | boolean | `false` | Use native OS font rendering (loose glyph bounds) |
| `glyph.text` | String | `""` | Literal characters to include. Newlines escaped as `\n` |

### Render Type

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `render_type` | int | `2` | Rendering engine: `0`=Java, `1`=Native, `2`=FreeType |

The `RenderType` enum in `UnicodeFont.java`:
- `Java` (0) — Java2D rendering, supports effects
- `Native` (1) — OS native rendering, loose glyph bounds
- `FreeType` (2) — FreeType rendering, best hinting, no Java effects

---

## Effects System

Effects are serialized as repeating groups. Each `effect.class` line starts a new effect, and subsequent `effect.<ValueName>` lines set properties on that effect.

```
effect.class=<fully-qualified-Java-class-name>
effect.<ValueName>=<serialized-value>
```

### Value Serialization Formats

| Value Type | Serialized Format | Example |
|------------|-------------------|---------|
| Color | 6-char hex RGB, no `#` prefix | `ff0000` (red) |
| int | Decimal integer string | `42` |
| float | Decimal float string | `2.5` |
| boolean | `true` or `false` | `true` |
| option | The option's int value string | `0` (Bevel join) |

Colors are serialized via `EffectUtil.toString(Color)` as exactly 6 lowercase hex chars (RRGGBB). Alpha is NOT stored. Parsing returns `Color.white` if string is null or not exactly 6 characters.

### ColorEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect`

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `ffffff` | Solid fill color |

### GradientEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.GradientEffect`

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Top color` | Color | `00ffff` | Gradient top color |
| `Bottom color` | Color | `0000ff` | Gradient bottom color |
| `Offset` | int | `0` | Vertical offset of gradient center |
| `Scale` | float | `1` | Height scaling percentage |
| `Cyclic` | boolean | `false` | Whether gradient repeats |

### OutlineEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineEffect`

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `000000` | Outline stroke color |
| `Width` | float | `2` | Stroke width (range 0.1-999) |
| `Join` | option | `0` (Bevel) | Corner join: `0`=Bevel, `2`=Miter, `1`=Round |

### OutlineWobbleEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineWobbleEffect`
Extends OutlineEffect.

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `000000` | Outline color |
| `Width` | float | `2` | Stroke width |
| `Detail` | float | `1` | Wobble sampling frequency (range 1-50) |
| `Amplitude` | float | `1` | Wobble magnitude (range 0.5-50) |

### OutlineZigzagEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineZigzagEffect`
Extends OutlineEffect.

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `000000` | Outline color |
| `Width` | float | `2` | Stroke width |
| `Wavelength` | float | `3` | Zigzag wavelength (range 1-100) |
| `Amplitude` | float | `1` | Zigzag amplitude (range 0.5-50) |

### ShadowEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.ShadowEffect`

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `000000` | Shadow color |
| `Opacity` | float | `0.6` | Translucency (range 0-1) |
| `X distance` | float | `2` | Horizontal offset pixels (range -99-99) |
| `Y distance` | float | `2` | Vertical offset pixels (range -99-99) |
| `Blur kernel size` | option | `0` (None) | Blur kernel: 0=None, 2..N |
| `Blur passes` | int | `1` | Number of blur iterations |

### DistanceFieldEffect

**Class:** `com.badlogic.gdx.tools.hiero.unicodefont.effects.DistanceFieldEffect`

| Value Name | Type | Default | Description |
|------------|------|---------|-------------|
| `Color` | Color | `ffffff` | Output color |
| `Scale` | int | `1` | Upscale factor for SDF computation (min 1) |
| `Spread` | float | `1.0` | Max edge distance for SDF (range 1.0-MAX_FLOAT) |

### Non-configurable Effects

- `FilterEffect` — programmatic image filter, no `getValues()`
- `Effect` — base interface
- `ConfigurableEffect` — configurable effect interface
- `EffectUtil` — serialization utility class

---

## Character / Glyph Set Specification

Hiero specifies characters as **literal text** in `glyph.text` — every unique character in the string gets a glyph. This differs from BMFont `.bmfc` which uses numeric Unicode codepoint ranges.

Predefined character set buttons in the UI:
- **NEHE**: Basic ASCII printable characters (codepoints 32–126)
- **ASCII**: NEHE plus extended Latin-1 characters (codepoints 33–255)
- **Extended**: A larger Unicode set defined as `EXTENDED_CHARS` constant

---

## Output Format

Hiero outputs **Angel Code BMFont text format** via `BMFontUtil.java`:
- `.fnt` file — text format with `info`, `common`, `page`, `chars`/`char`, and `kernings`/`kerning` blocks
- `.png` files — 32-bit ARGB texture pages

Known quirks:
- Hiero writes padding as `up,right,down,left` order (matching BMFont spec)
- `BitmapFontWriter` (a different libGDX class) incorrectly writes `up,down,left,right`
- Hiero can produce negative spacing values (e.g., `spacing=-2,-2`) which cannot be encoded in BMFont's binary format (unsigned int field)

---

## Key Differences: .hiero vs .bmfc

| Aspect | .hiero | .bmfc |
|--------|--------|-------|
| Format | `key=value` flat text | Custom multi-section text |
| Character specification | Literal text string | Numeric codepoint ranges (`chars=32-126`) |
| Effects | Java class-based with named values | Separate config sections |
| Font source | System font name OR file path | Font descriptor string |
| Rendering engine | Java2D / FreeType / Native selectable | FreeType with supersampling |
| Texture size | Single width+height pair | Separate width/height |
| Multiple effects | Repeating `effect.class` blocks | Fixed set of built-in options |
| Encoding | UTF-8 | ASCII with codepoint numbers |
| Line breaks in char set | Escaped `\n` | N/A (uses numeric ranges) |

---

## Complete Example .hiero File

```
font.name=Arial
font.size=32
font.bold=false
font.italic=false
font.gamma=1.8
font.mono=false

font2.file=
font2.use=false

pad.top=1
pad.right=1
pad.bottom=1
pad.left=1
pad.advance.x=-2
pad.advance.y=-2

glyph.native.rendering=false
glyph.page.width=512
glyph.page.height=512
glyph.text=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890 \n"!`?'.,;:()[]{}<>|/@\^$-%+=#_&~*

render_type=2

effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect
effect.Color=ffffff

effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineEffect
effect.Color=000000
effect.Width=2.0
effect.Join=0

effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ShadowEffect
effect.Color=000000
effect.Opacity=0.6
effect.X distance=2.0
effect.Y distance=2.0
effect.Blur kernel size=0
effect.Blur passes=1
```

---

## Source Code References

All files in `libgdx/libgdx` on GitHub under `extensions/gdx-tools/src/com/badlogic/gdx/tools/hiero/`:

| File | Purpose |
|------|---------|
| `HieroSettings.java` | **Definitive .hiero format parser/writer** |
| `Hiero.java` | Main UI, open/save, effect registration, CLI args |
| `BMFontUtil.java` | Writes .fnt + .png output |
| `Kerning.java` | Extracts kerning from fonts |
| `unicodefont/UnicodeFont.java` | RenderType enum, glyph rasterization |
| `unicodefont/effects/ConfigurableEffect.java` | Effect + Value interfaces |
| `unicodefont/effects/EffectUtil.java` | Value serialization (hex color, int, float, bool, option) |
| `unicodefont/effects/ColorEffect.java` | Solid color fill |
| `unicodefont/effects/GradientEffect.java` | Gradient fill |
| `unicodefont/effects/OutlineEffect.java` | Stroke outline |
| `unicodefont/effects/OutlineWobbleEffect.java` | Wobbly outline |
| `unicodefont/effects/OutlineZigzagEffect.java` | Zigzag outline |
| `unicodefont/effects/ShadowEffect.java` | Drop shadow with blur |
| `unicodefont/effects/DistanceFieldEffect.java` | SDF generation |

### External References

- libGDX Hiero Wiki: `https://libgdx.com/wiki/tools/hiero`
- Hiero GitHub source: `https://github.com/libgdx/libgdx/tree/master/extensions/gdx-tools/src/com/badlogic/gdx/tools/hiero`
- BMFont padding order fix PR: `https://github.com/libgdx/libgdx/pull/4297`
- Original Hiero thread: `https://jvm-gaming.org/t/hiero-bitmap-font-tool/29151`

---

## Property Mapping: .hiero → KernSmith FontGeneratorOptions

This table maps Hiero properties to their KernSmith equivalents for implementing the config reader/writer.

| Hiero Property | KernSmith Equivalent | Notes |
|----------------|---------------------|-------|
| `font.name` | `BmfcConfig.FontName` | System font lookup |
| `font2.file` + `font2.use` | `BmfcConfig.FontFile` | File-based font; use if `font2.use=true` |
| `font.size` | `FontGeneratorOptions.Size` | Direct mapping |
| `font.bold` | `FontGeneratorOptions.Bold` | Direct mapping |
| `font.italic` | `FontGeneratorOptions.Italic` | Direct mapping |
| `font.gamma` | — | No direct equivalent; could add |
| `font.mono` | `FontGeneratorOptions.AntiAlias` (inverted) | `mono=true` → `AntiAlias=AntiAliasMode.None` |
| `pad.top/right/bottom/left` | `FontGeneratorOptions.Padding` | Direct mapping |
| `pad.advance.x` | `FontGeneratorOptions.Spacing.X` | Maps to spacing |
| `pad.advance.y` | `FontGeneratorOptions.Spacing.Y` | Maps to spacing |
| `glyph.page.width` | `FontGeneratorOptions.MaxTextureWidth` | Direct mapping |
| `glyph.page.height` | `FontGeneratorOptions.MaxTextureHeight` | Direct mapping |
| `glyph.text` | `FontGeneratorOptions.Characters` | Literal chars → CharacterSet |
| `render_type` | — | KernSmith always uses FreeType |
| `effect.class=...ColorEffect` | `FontGeneratorOptions.Outline*` (none) | Just a fill color; default behavior |
| `effect.class=...GradientEffect` | `FontGeneratorOptions.Gradient*` | Map top/bottom color; Hiero's Offset/Scale/Cyclic have no direct KernSmith equivalent (GradientAngle/GradientMidpoint are different concepts) |
| `effect.class=...OutlineEffect` | `FontGeneratorOptions.Outline*` | Map color, width; join has no KernSmith equivalent (Hiero-specific, dropped on import); Hiero Width is float (truncated to int for KernSmith Outline) |
| `effect.class=...ShadowEffect` | `FontGeneratorOptions.Shadow*` | Map color (ShadowR/G/B), offset, opacity; Hiero's two-param blur (kernel size + passes) collapses to single ShadowBlur |
| `effect.class=...DistanceFieldEffect` | `FontGeneratorOptions.Sdf` | Hiero's Scale and Spread have no KernSmith equivalent (Sdf is boolean only) |
| `effect.class=...OutlineWobbleEffect` | — | No KernSmith equivalent |
| `effect.class=...OutlineZigzagEffect` | — | No KernSmith equivalent |
| `glyph.native.rendering` | — | KernSmith always uses FreeType |
