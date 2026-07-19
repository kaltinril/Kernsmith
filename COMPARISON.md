# Bitmap Font Generator Comparison

How does KernSmith compare to other bitmap font generators?

## Platform & Licensing

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Windows** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ✅ |
| **macOS** | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Linux** | ✅ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Web/WASM** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **License** | MIT | Free | Apache 2.0 | MIT | $9.99 | Paid | Free |
| **Open Source** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | ⚠️ |
| **Still Maintained** | ✅ | ⚠️ | ⚠️ | ✅ | ❌ | ⚠️ | ❌ |

## Usage Model

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **In-Memory API** | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Library / SDK** | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **CLI Tool** | ✅ | ✅ | ⚠️ | ✅ | ❌ | ❌ | ❌ |
| **GUI** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| **Embeddable in Pipelines** | ✅ | ⚠️ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Batch Processing** | ✅ | ✅ | ⚠️ | ✅ | ❌ | ❌ | ⚠️ |

## Font Input Support

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **TTF/OTF** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **WOFF** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **WOFF2** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Variable Fonts** | ✅ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Color Fonts (COLR/CPAL)** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Emoji** | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **System Font Discovery** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Byte Stream Input** | ✅ | ❌ | ❌ | ⚠️ | ❌ | ❌ | ❌ |

## Rasterization

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Rasterizer** | FreeType + GDI + DWrite + Stb | GDI + TT outline | Java AWT / FreeType | msdfgen (FreeType) | Core Text | Core Text | Flash |
| **Pluggable Rasterizers** | ✅ | ❌ | ⚠️ | ❌ | ❌ | ❌ | ❌ |
| **Anti-Aliasing** | 4 modes | Multiple modes | Basic | N/A | Basic | Basic | Basic |
| **Super Sampling** | 1-4x | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **SDF** | ✅ | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **MSDF** | 🔜 | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Hinting Control** | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **MTSDF** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |

## Effects

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Outline** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Shadow** | ✅ | ❌ | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Gradient** | ✅ | ❌ | ✅ | ❌ | ✅ | ✅ | ❌ |
| **Texture Fill** | 🔜 | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ |
| **Synthetic Bold/Italic** | ✅ | ❌ | ✅ | ❌ | ✅ | ❌ | ❌ |
| **Layered/Composited** | ✅ | ⚠️ | ✅ | ❌ | ⚠️ | ⚠️ | ❌ |
| **Inner Stroke** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Inner Shadow** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Wobble/Zigzag Outline** | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Volumetric/3D Effect** | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Glossy Fill** | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |

## Kerning & Metrics

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **GPOS Kerning** | ✅ | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ |
| **Kern Table (legacy)** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| **OS/2 Metrics** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Extended Metadata** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Font Subsetting** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Optical Kerning** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ⚠️ |

## Atlas & Output

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **PNG** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **TGA** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **DDS** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Channel Packing** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **MaxRects Packing** | ✅ | ❌ | ❌ | ❌ | ❓ | ❓ | ❓ |
| **Skyline Packing** | ✅ | ❌ | ❌ | ❌ | ❓ | ❓ | ❓ |
| **Multi-Page Atlas** | ✅ | ✅ | ✅ | ❌ | ❓ | ❌ | ❌ |
| **Auto Size Estimation** | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **.fnt Text** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| **.fnt XML** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ❌ |
| **.fnt Binary** | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | ❌ |
| **DDS Compression (DXT)** | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **TGA RLE Compression** | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **BMP** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **TIFF** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **JSON Metadata** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **CSV Metadata** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Artery Font (.arfont)** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **Custom Output Templates** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| **.bmfc Config** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **.hiero Config** | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |

## Additional Features

| | KernSmith | BMFont | Hiero | msdf-atlas-gen | bmGlyph | Glyph Designer | ShoeBox |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Custom Glyph Images** | ✅ | ✅ | ❌ | ❌ | ✅ | ✅ | ❌ |
| **Font Fallback/Substitution** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| **Multi-Font Atlas** | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| **GUI** | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ |
| **Engine-Specific Exports** | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |

## Config Format Compatibility

KernSmith now supports the libGDX **Hiero `.hiero`** format for interop: it can both **read** Hiero configs and **export** to them, in addition to its native BMFont `.bmfc` format. When loading, the format is auto-detected by inspecting the file content (the extension is only a fallback tiebreaker); when saving, the format is selected by the file extension. `.bmfc` is the native, lossless format that covers every KernSmith setting; `.hiero` is supported for interoperability and is lossy for KernSmith-only settings that Hiero cannot represent (e.g. spacing, gradient angle, hinting, hard shadow, super sampling, channel packing, color fonts, forced synthetic bold/italic, autofit texture). A native `.kern` format is proposed to cover all features without limitations.

| Feature | .bmfc | .hiero | .kern |
|---|:---:|:---:|:---:|
| | | | *(proposed)* |
| **Basic Font Settings** | | | |
| Font path/name | ✅ | ✅ | 🔜 |
| Font size | ✅ | ✅ | 🔜 |
| Bold / Italic | ✅ | ✅ | 🔜 |
| Force synthetic bold/italic | ✅ | ❌ | 🔜 |
| Match char height | ✅ | ❌ | 🔜 |
| Height percent (stretch) | ✅ | ❌ | 🔜 |
| Face index (TTC/OTC) | ✅ | ❌ | 🔜 |
| DPI | ✅ | ❌ | 🔜 |
| **Rendering** | | | |
| Anti-aliasing mode | ✅ | ✅ | 🔜 |
| Super sampling | ✅ | ❌ | 🔜 |
| Hinting | ✅ | ❌ | 🔜 |
| SDF mode + spread | ✅ | ✅ | 🔜 |
| Rasterizer backend | ✅ | 🔜 | 🔜 |
| ClearType | 🔜 | ❌ | 🔜 |
| Render from outline | 🔜 | ❌ | ❌ |
| Gamma | ❌ | 🔜 | 🔜 |
| Mono (no AA) | ❌ | 🔜 | 🔜 |
| **Effects** | | | |
| Outline width | ✅ | ✅ | 🔜 |
| Outline color | ✅ | ✅ | 🔜 |
| Outline join type | ❌ | 🔜 | 🔜 |
| Gradient (colors/angle/midpoint) | ✅ | ✅ | 🔜 |
| Shadow (offset/blur/color) | ✅ | ✅ | 🔜 |
| Shadow opacity | ❌ | 🔜 | 🔜 |
| Hard shadow | ❌ | ❌ | 🔜 |
| Texture fill | ❌ | ❌ | 🔜 |
| Wobble/zigzag outline | ❌ | 🔜 | 🔜 |
| Effect stacking/ordering | ❌ | 🔜 | 🔜 |
| **Variable / Color Fonts** | | | |
| Variable font axes | ❌ | ❌ | 🔜 |
| Color font mode | ✅ | ❌ | 🔜 |
| Color palette index | ✅ | ❌ | 🔜 |
| **Atlas / Packing** | | | |
| Texture size (W x H) | ✅ | ✅ | 🔜 |
| Packing algorithm | ✅ | ❌ | 🔜 |
| Padding (4 sides) | ✅ | ✅ | 🔜 |
| Padding advance X/Y | ❌ | 🔜 | 🔜 |
| Spacing (H/V) | ✅ | ❌ | 🔜 |
| Channel packing | ✅ | ❌ | 🔜 |
| Per-channel config | 🔜 | ❌ | 🔜 |
| Channel inversion | 🔜 | ❌ | 🔜 |
| Power of two | ✅ | ❌ | 🔜 |
| Autofit texture | ✅ | ❌ | 🔜 |
| Equalize cell heights | ✅ | ❌ | 🔜 |
| Force zero offsets | ✅ | ❌ | 🔜 |
| Size constraints | ❌ | ❌ | 🔜 |
| **Output** | | | |
| Output format (text/xml/bin) | ✅ | ❌ | 🔜 |
| Texture format (png/tga/dds) | ✅ | ❌ | 🔜 |
| Texture compression (DXT) | 🔜 | ❌ | 🔜 |
| Output bit depth | 🔜 | ❌ | 🔜 |
| Output path | ✅ | ✅ | 🔜 |
| **Characters** | | | |
| Character set / ranges | ✅ | ✅ | 🔜 |
| Kerning on/off | ✅ | ❌ | 🔜 |
| Fallback character | ✅ | ❌ | 🔜 |
| Disable box chars | 🔜 | ❌ | 🔜 |
| **Advanced** | | | |
| Custom glyph images | 🔜 | ❌ | 🔜 |
| Target region | ❌ | ❌ | 🔜 |
| Collect metrics | ❌ | ❌ | 🔜 |

## Rasterizer Backend Comparison

KernSmith supports four pluggable rasterizer backends. Each has different platform support, feature coverage, and trade-offs.

### Platform Support

| | FreeType | GDI | DirectWrite | StbTrueType |
|---|:---:|:---:|:---:|:---:|
| **Windows** | ✅ | ✅ | ✅ | ✅ |
| **Linux** | ✅ | ❌ | ❌ | ✅ |
| **macOS** | ✅ | ❌ | ❌ | ✅ |
| **Blazor WASM** | ❌ | ❌ | ❌ | ✅ |
| **NativeAOT** | ⚠️ | ⚠️ | ⚠️ | ✅ |
| **Android** | ✅ | ❌ | ❌ | ✅ |
| **iOS** | ⚠️ | ❌ | ❌ | ✅ |
| **Serverless / Containers** | ✅ | ❌ | ❌ | ✅ |
| **Console (Xbox/PS/Switch)** | ❌ | ❌ | ❌ | ✅ |
| **Native Dependencies** | FreeType native libs | Win32 GDI | Win32 DirectWrite | None |
| **Trimming Safe** | ⚠️ | N/A | N/A | ✅ |
| **AOT Compatible** | ⚠️ | N/A | N/A | ✅ |

### Feature Support

| | FreeType | GDI | DirectWrite | StbTrueType |
|---|:---:|:---:|:---:|:---:|
| **TTF** | ✅ | ✅ | ✅ | ✅ |
| **OTF (CFF)** | ✅ | ✅ | ✅ | ❌ |
| **WOFF** | ✅ | ❌ | ✅ | ❌ |
| **WOFF2** | ❌ | ❌ | ❌ | ❌ |
| **TTC (font collections)** | ✅ | ❌ | ✅ | ✅ |
| **Anti-aliasing** | Grayscale, Light, LCD, None | Grayscale, None | None, Grayscale | Grayscale, None |
| **SDF Rendering** | ✅ | ❌ | ❌ | ✅ |
| **Hinting** | ✅ | ✅ | ✅ | ❌ |
| **Synthetic Bold** | ✅ | ✅ | ✅ | ✅ |
| **Synthetic Italic** | ✅ | ✅ | ✅ | ✅ |
| **Outline Stroke** | ✅ | ❌ | ❌ | ❌ |
| **Super Sampling** | ✅ | ✅ | ✅ | ✅ |
| **Color Fonts (COLR/CPAL)** | ✅ | ❌ | ❌ | ❌ |
| **Variable Fonts** | ✅ | ❌ | ❌ | ❌ |
| **System Font Loading** | ❌ | ✅ | ✅ | ❌ |
| **BMFont.exe Parity** | ❌ | ✅ | ❌ | ❌ |

### When to Use

- **FreeType** -- Default for most use cases. Cross-platform, full-featured, industry-standard quality.
- **GDI** -- Pixel-perfect BMFont.exe compatibility on Windows. Use for validating against BMFont reference output.
- **DirectWrite** -- Windows system-font loading with ClearType-hinted grayscale rendering. High-quality Windows text output. (For color fonts or variable fonts, use FreeType.)
- **StbTrueType** -- Blazor WASM, NativeAOT, iOS, consoles, or anywhere native libraries are unavailable. Pure C#, zero dependencies.

## Tool Descriptions

- **[KernSmith](https://github.com/kaltinril/Kernsmith)** -- Cross-platform .NET library and CLI for generating BMFont-compatible bitmap fonts. In-memory API, pluggable rasterizers, layered effects. Reads and writes both its native BMFont `.bmfc` config and the libGDX **Hiero `.hiero`** config format for interop. MIT licensed.
- **[BMFont (AngelCode)](https://www.angelcode.com/products/bmfont/)** -- The original Windows-only tool that defined the `.fnt` format. Open source (zlib license, hosted on SourceForge). Includes both a GUI and CLI executable (`bmfont.com`). Uses GDI and TrueType outline rasterization with multiple anti-aliasing modes and super sampling. Last updated with v1.14b beta in 2025.
- **[Hiero](https://libgdx.com/wiki/tools/hiero)** -- LibGDX's open-source Java bitmap font generator. Supports both Java AWT and FreeType rendering backends, with outline, shadow, gradient, and SDF effects. Cross-platform via Java.
- **[msdf-atlas-gen](https://github.com/Chlumsky/msdf-atlas-gen)** -- Cross-platform C++ CLI tool and in-memory library API focused on multi-channel signed distance field atlas generation. Built on msdfgen with FreeType for glyph loading. Outputs JSON, CSV, and Artery Font formats. Strong SDF/MSDF support but no traditional rasterization or effects.
- **[bmGlyph](https://www.bmglyph.com/)** -- Paid macOS app for bitmap font generation with effects including texture fill. Uses Core Text rasterizer.
- **[Glyph Designer](https://71squared.com/glyphdesigner)** -- Paid macOS app with outline, shadow, gradient, and texture fill effects. The Windows version has been discontinued; macOS only going forward. Last release June 2024.
- **[ShoeBox](https://renderhjs.net/shoebox/)** -- Free Adobe AIR tool with bitmap font generation among other sprite/atlas utilities. Source code published on GitHub in 2024 (no license). Adobe AIR dependency limits its future.

## Legend

| Icon | Comparison Tables | Config Format Tables |
|:---:|---|---|
| ✅ | Supported | Supported and part of the format |
| ❌ | Not supported | Not supported and not part of the format |
| 🔜 | Planned | Planned support, part of the format |
| ⚠️ | Partial / Limited | No plan to support, but part of the format |
| ❓ | Unknown (closed source) | — |
