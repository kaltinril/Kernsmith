# bmfontier -- Project Vision & Scope

> This is a living reference document that captures the purpose, vision, and scope of the bmfontier project based on the creator's stated goals. It is not marketing copy.

---

## What Is bmfontier?

bmfontier is a C# NuGet package that reads font files (including system-installed fonts) and generates BMFont-format output: `.fnt` descriptor files and `.png` texture atlas images.

## The Problem

Today, generating BMFont files requires:

- **The BMFont tool** -- a Windows-only desktop application with no programmatic API.
- **A manual workflow**: open the tool, configure settings, export, then copy files into the project.
- **No runtime generation** -- fonts must be pre-baked at build time.
- **No cross-platform solution.**
- **No way to integrate font generation into automated pipelines** without wrapping a GUI tool (as Gum does with bmfont.exe).

Game developers and UI framework authors who use bitmap fonts are stuck with offline tooling.

## The Vision

bmfontier is a **library-first** package. It is not a tool -- it is the engine that tools are built on.

### Core Capabilities (the NuGet package itself)

1. **Read font files** -- Parse TrueType (.ttf), OpenType (.otf), font collections (.ttc/.otc), and potentially WOFF/WOFF2.
2. **Read system fonts** -- Enumerate and load fonts installed on the system (Windows, macOS, Linux).
3. **Rasterize glyphs** -- Render font glyphs to bitmaps with configurable size, anti-aliasing, and quality settings.
4. **Generate BMFont output** -- Produce .fnt descriptor (text, XML, or binary format) and .png texture atlas pages.
5. **All in memory** -- The entire pipeline can run without touching disk. No temp files. Input bytes in, BMFont data out.

The in-memory design is critical for:

- Game engines generating fonts on the fly at runtime.
- Server-side font generation (web apps, APIs).
- Environments where disk access is restricted or slow.

### What Others Build On Top

The NuGet package is the foundation. Others can build:

- **CLI tool** -- A cross-platform command-line BMFont generator (replacement for bmfont.exe).
- **Desktop app / EXE** -- A GUI font generator with preview.
- **Website** -- An online BMFont generator (upload font, download .fnt + .png).
- **Game engine integration** -- Runtime font generation in MonoGame, FNA, Godot C#, Unity, etc.
- **CI/CD pipeline step** -- Automated font generation during build.
- **UI frameworks** -- Libraries like Gum could use bmfontier instead of wrapping bmfont.exe.

### Design Principles

- **Efficient** -- Minimal allocations, fast rasterization, smart texture packing. Suitable for runtime use in games.
- **Fast** -- Generating a standard ASCII font at a single size should be near-instant.
- **Maintainable** -- Clean C# codebase, well-documented, easy to contribute to.
- **Cross-platform** -- Runs anywhere .NET runs (Windows, macOS, Linux, mobile, WASM).
- **Library-first** -- No UI, no CLI, no opinions about how the output is used. Pure API.
- **Zero disk I/O by default** -- Streams and byte arrays in, structured data out. Disk is opt-in.
- **BMFont format compatibility** -- Output must be consumable by any existing BMFont reader (MonoGame.Extended, libGDX, Gum, Godot, Cocos2d, etc.).

---

## Scope

### In Scope -- Phase 1 (Core)

- TTF and OTF font file parsing (or delegated to a dependency).
- System font enumeration and loading.
- Glyph rasterization to bitmap (grayscale anti-aliased).
- Texture atlas packing (efficient bin packing).
- BMFont .fnt generation (text format at minimum, ideally all three formats).
- BMFont .png atlas generation (in-memory).
- Kerning pair extraction and export.
- Configurable character sets (ASCII, extended ASCII, Unicode ranges, custom lists).
- Configurable font size, padding, spacing.
- API designed for both streaming/in-memory and file-based workflows.

### In Scope -- Phase 2 (Extended)

- WOFF/WOFF2 decompression and reading.
- Font collection (.ttc/.otc) support with font selection.
- Variable font support (axis selection).
- Outline/border generation.
- SDF (Signed Distance Field) generation.
- Multiple output formats (text, XML, binary .fnt).
- Advanced texture packing options.
- Channel packing (multiple glyphs per RGBA channel).

### In Scope -- Phase 3 (Ecosystem)

- Color font support (COLRv0, sbix, CBDT).
- Reference CLI tool implementation.
- Performance benchmarks and optimization.
- Font subsetting (only include needed glyphs).

### Out of Scope

- Being a font rendering engine (not for drawing text on screen).
- Being a text layout/shaping engine (no line breaking, no BiDi, no complex script shaping).
- GUI application (that is for others to build).
- Web frontend (that is for others to build).
- Font editing or modification.
- Non-BMFont output formats (unless community demand warrants it).

---

## Target Audience

- **Game developers** using bitmap fonts (MonoGame, FNA, Godot, custom engines).
- **UI framework authors** who need font atlas generation (Gum, custom UI systems).
- **Tool builders** who want to create BMFont utilities.
- **DevOps/CI teams** automating asset pipelines.

---

## Success Criteria

- Drop-in NuGet package: `dotnet add package bmfontier`.
- Generate a BMFont from a TTF in under 10 lines of code.
- Output is byte-compatible with existing BMFont readers.
- Zero disk I/O path works end-to-end.
- Cross-platform: works on Windows, macOS, Linux without native dependencies (stretch goal -- may need native dependencies for rasterization).

---

## Key Technical Decisions (To Be Made)

- **Font parsing**: Custom minimal parser vs. SixLabors.Fonts vs. SkiaSharp vs. FreeType bindings?
- **Rasterization**: Custom vs. SkiaSharp vs. System.Drawing vs. SixLabors.ImageSharp?
- **Texture packing algorithm**: MaxRects, shelf packing, or other?
- **Target framework**: .NET 6+? .NET Standard 2.1?
- **Native dependencies**: Acceptable? (affects cross-platform story)
- **Licensing**: What license for bmfontier itself?

---

## Related References

- [BMFont Format Reference](bmfont-format-reference.md)
- [TTF Font Reference](ttf-font-reference.md)
- [Other Font Formats Reference](other-font-formats-reference.md)
