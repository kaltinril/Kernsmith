# Phase 16 — BMFont .bmfc Format Compatibility

> **Status**: Complete
> **Created**: 2026-03-20
> **Goal**: Replace our custom INI-style .bmfc format with full compatibility for the AngelCode BMFont .bmfc format, so users can use existing .bmfc files from BMFont and vice versa.

---

## Problem

Our `.bmfc` parser uses a custom INI-style format with `[sections]`:
```
[font]
system-font = Arial
[rendering]
size = 32
[atlas]
max-texture-size = 512
```

The industry standard BMFont `.bmfc` format uses flat `key=value` pairs:
```
fontName=Arial
fontSize=32
outWidth=256
outHeight=256
paddingUp=0
paddingDown=0
fontDescFormat=0
textureFormat=tga
chars=32-126
```

These are **completely incompatible**. Users with existing BMFont .bmfc files can't use them with KernSmith, and our .bmfc files don't work with BMFont. Since we're positioning as a BMFont-compatible tool, this is a significant interop gap.

## BMFont .bmfc Format Reference

From `C:\Users\jerem\Downloads\bmfont64_1.14b_beta\bmfont.bmfc`:

```
# AngelCode Bitmap Font Generator configuration file
fileVersion=1

# font settings
fontName=Arial
fontFile=
charSet=0
fontSize=32
aa=1
scaleH=100
useSmoothing=1
isBold=0
isItalic=0
useUnicode=1
disableBoxChars=1
outputInvalidCharGlyph=0
dontIncludeKerningPairs=0
useHinting=1
renderFromOutline=0
useClearType=1
autoFitNumPages=0
autoFitFontSizeMin=0
autoFitFontSizeMax=0

# character alignment
paddingDown=0
paddingUp=0
paddingRight=0
paddingLeft=0
spacingHoriz=1
spacingVert=1
useFixedHeight=0
forceZero=0
widthPaddingFactor=0.00

# output file
outWidth=256
outHeight=256
outBitDepth=8
fontDescFormat=0
fourChnlPacked=0
textureFormat=tga
textureCompression=0
alphaChnl=1
redChnl=0
greenChnl=0
blueChnl=0
invA=0
invR=0
invG=0
invB=0

# outline
outlineThickness=0

# selected chars
chars=32-126

# imported icon images
```

### Key Mappings (BMFont -> KernSmith)

| BMFont Key | BMFont Values | KernSmith Equivalent |
|-----------|---------------|---------------------|
| `fontName` | font family name | `--system-font` |
| `fontFile` | path to .ttf | `--font` |
| `fontSize` | negative = px from top | `--size` (always positive px) |
| `isBold` | 0/1 | `--bold` |
| `isItalic` | 0/1 | `--italic` |
| `aa` | 1=normal, 2=ClearType | `--aa grayscale` / `--aa lcd` |
| `useSmoothing` | 0/1 | `--mono` (inverted) |
| `useHinting` | 0/1 | `--hinting` / `--no-hinting` |
| `paddingUp/Down/Right/Left` | pixels | `--padding` |
| `spacingHoriz/Vert` | pixels | `--spacing` |
| `outWidth` | texture width | `--max-texture-width` |
| `outHeight` | texture height | `--max-texture-height` |
| `fontDescFormat` | 0=text, 1=xml, 2=binary | `--format` |
| `textureFormat` | png/tga/dds | `--texture-format` |
| `fourChnlPacked` | 0/1 | `--channel-pack` |
| `outlineThickness` | pixels | `--outline` |
| `useFixedHeight` | 0/1 | `--equalize-heights` |
| `forceZero` | 0/1 | `--force-offsets-zero` |
| `chars` | char codes/ranges | `--charset` |
| `dontIncludeKerningPairs` | 0/1 | `--no-kerning` (inverted) |
| `scaleH` | percentage | `--height-percent` |
| `autoFitNumPages` | 0=off, N=target pages | `--autofit` |
| `outBitDepth` | 8/32 | grayscale vs RGBA |

### KernSmith Extensions (not in BMFont)

These are KernSmith-specific features with no BMFont equivalent:
- Gradient (top/bottom colors, angle, midpoint)
- Shadow (offset, color, blur, opacity)
- Outline color (BMFont only has thickness)
- SDF rendering
- Super sampling
- Variable font axes
- Color font / palette selection
- Skyline packer selection
- Multiple packer algorithms

---

## Design

### Approach: Read both formats, write BMFont format

1. **Auto-detect format** on read: if file contains `[section]` headers, use legacy parser; if flat `key=value`, use BMFont parser
2. **Write BMFont format** by default from `--save-config`
3. **Support KernSmith extensions** via `# KernSmith:` prefixed comments (ignored by BMFont but preserved by KernSmith)
4. **Deprecate** our custom INI format — warn when reading it, suggest migration

### Extension Keys

For KernSmith-specific features, add new `key=value` pairs in the same flat style. BMFont will ignore keys it doesn't recognize:

```
# KernSmith extensions
gradientTop=FF0000
gradientBottom=FFD700
gradientAngle=90
gradientMidpoint=0.5
shadowOffsetX=3
shadowOffsetY=3
shadowColor=000000
shadowBlur=3
outlineColor=1A0500
useSdf=0
superSample=1
packer=maxrects
```

BMFont ignores unknown keys, so these files remain compatible. Our parser reads them all.

---

## Tasks

### Phase 1 — BMFont Format Reader
- [ ] Create `BmfcBmFontParser.cs` that reads the standard BMFont .bmfc format
- [ ] Map all BMFont keys to CliOptions
- [ ] Handle `chars=` format (comma-separated codes and ranges like `32-126,160-255`)
- [ ] Handle `fontSize` sign convention (BMFont uses negative for "match char height")
- [ ] Parse `# KernSmith:` extension comments

### Phase 2 — Auto-Detection
- [ ] Update `BmfcParser.Parse()` to auto-detect format (check for `[` section headers)
- [ ] Route to appropriate parser based on detection
- [ ] Warn when legacy INI format is detected: "Consider migrating to standard BMFont .bmfc format"

### Phase 3 — BMFont Format Writer
- [ ] Update `BmfcWriter` to output standard BMFont flat `key=value` format
- [ ] Include KernSmith extension keys as additional `key=value` pairs
- [ ] Preserve round-trip compatibility: read BMFont .bmfc -> save -> identical output

### Phase 4 — Migrate Test Files
- [ ] Convert all 32 test .bmfc files from INI to BMFont format
- [ ] Create BMFont-native .bmfc files for the 18 basic tests (for test_bmfont_bmfc.bat)
- [ ] Verify batch command works with new format
- [ ] Verify --save-config produces valid BMFont .bmfc files

### Phase 5 — Documentation
- [ ] Update CLI help text to reference BMFont .bmfc format
- [ ] Add format documentation to CLI README
- [ ] Update CLAUDE.md if needed

---

## Success Criteria

1. `KernSmith generate --config file.bmfc` works with BMFont-generated .bmfc files
2. `KernSmith generate --save-config out.bmfc` produces files BMFont can read (minus extensions)
3. KernSmith extensions round-trip through `# KernSmith:` comments
4. Legacy INI .bmfc files still work (with deprecation warning)
5. All 32 test .bmfc files converted to standard format

---

## Related Plans

- **phase-13-batch-cli.md** — batch command uses .bmfc files heavily
- **phase-15-library-performance.md** — batch API processes .bmfc-derived jobs
