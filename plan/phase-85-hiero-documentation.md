# Phase 85 — Hiero Documentation Updates

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phases 82, 83, 84
> **Goal**: Update all documentation, README, samples, and NuGet metadata to reflect `.hiero` format support.

---

## Files Requiring Updates

### 1. README.md (Root)

**Lines 13–14 — Feature list:**
Add `.hiero` (Hiero/libGDX) to supported config formats alongside `.bmfc`.

**Lines 51–58 — Quick Start:**
Add example showing `.hiero` usage:
```csharp
// From Hiero config
var result = BmFont.FromConfig("myfont.hiero");
```

**Lines 69–70 — ToFile output:**
Mention that `.ToFile()` can optionally save `.hiero` alongside `.fnt` + images.

**Lines 108–115 — Fluent Builder:**
Add `FromConfig("base.hiero")` example.

**Lines 299–323 — Write to Disk / In-Memory:**
Document `ToHiero()` method alongside `ToBmfc()`.

**Lines 356–374 — Reading and Writing Config Files:**
Expand section to cover both formats:
```csharp
// Read any supported config format (auto-detected by extension)
var config = ConfigFormatFactory.ReadConfig("project.hiero");
var config = ConfigFormatFactory.ReadConfig("project.bmfc");

// Write to specific format
ConfigFormatFactory.WriteConfig(config, "output.hiero");
ConfigFormatFactory.WriteConfig(config, "output.bmfc");

// Format-specific readers still available
var hieroConfig = HieroConfigReader.Read("project.hiero");
var bmfcConfig = BmfcConfigReader.Read("project.bmfc");
```

### 2. NuGet Package Metadata

**File:** `src/KernSmith/KernSmith.csproj`

**Line 7 — Description:**
```xml
<Description>BMFont-compatible bitmap font atlases from TTF/OTF/WOFF files. Supports BMFont .bmfc and Hiero .hiero configuration formats.</Description>
```

**Line 13 — PackageTags:**
Add `hiero` and `libgdx` tags:
```xml
<PackageTags>bmfont;bitmap-font;hiero;libgdx;font-atlas;...</PackageTags>
```

### 3. CLI README

**File:** `tools/KernSmith.Cli/README.md`

**Lines 187–195 — Config flags:**
Update `--config` and `--save-config` descriptions to mention both formats.

**Lines 198–223 — init Command:**
Add `.hiero` example:
```bash
kernsmith init -o myfont.hiero --font "Arial" --size 32
```

**Lines 308–330 — batch Command:**
Document mixed-format batch processing:
```bash
kernsmith batch configs/*.bmfc configs/*.hiero --parallel 4
```

**Lines 420–496 — Config Files section:**
Add new subsection documenting `.hiero` format structure with example.

### 4. CLI Docs

**File:** `docs/cli/index.md` (lines 34–57)
- Update command table to mention `.hiero` in init and batch descriptions
- Update Configuration Files section

**File:** `docs/cli/commands.md` (lines 134–186)
- Update `--config` and `--save-config` flag docs
- Update init command docs
- Update batch command docs

### 5. Core Library Docs

**File:** `docs/core/index.md` (line 33)
- Update `FromConfig()` description: "generate from a `.bmfc` or `.hiero` configuration file"

### 6. Sample Code

**File:** `samples/KernSmith.Samples/Program.cs`

**Lines 42–55 — Section 2:**
Add Hiero example alongside existing `.bmfc` example:
```csharp
// Section 2b: FromConfig (.hiero)
var hieroPath = Path.Combine(samplesDir, "sample.hiero");
if (File.Exists(hieroPath))
{
    var hieroResult = BmFont.FromConfig(hieroPath);
    Console.WriteLine($"Generated from .hiero: {hieroResult.Pages.Count} page(s)");
}
```

**Line 111 — Export:**
Add `ToHiero()` example:
```csharp
var hieroString = memResult.ToHiero();
Console.WriteLine($"Hiero config: {hieroString.Length} chars");
```

### 7. CHANGELOG.md

Add to the next release section:
```markdown
### Added
- Hiero `.hiero` configuration file format support (read and write)
- `HieroConfigReader` and `HieroConfigWriter` classes
- `ConfigFormatFactory` for auto-detecting config format by file extension
- `BmFontResult.ToHiero()` for exporting Hiero config strings
- `BmFont.FromConfig()` and `BmFontBuilder.FromConfig()` now auto-detect `.hiero` and `.bmfc`
- CLI: `--config` and `--save-config` accept `.hiero` files
- CLI: `init` command can create `.hiero` configs
- CLI: `batch` command processes mixed `.bmfc` and `.hiero` files
- UI: Load/Save project dialogs accept `.hiero` files
- UI: Drag-and-drop supports `.hiero` files
```

### 8. Sample .hiero File

Create a sample `.hiero` file for the samples directory:

**New file:** `samples/KernSmith.Samples/sample.hiero`
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
glyph.text=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890 !"#$%&'()*+,-./:;<=>?@[\]^_`{|}~

render_type=2

effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect
effect.Color=ffffff
```

## Summary of All Documentation Changes

| File | Sections | Change Type |
|------|----------|-------------|
| `README.md` | Features, Quick Start, Builder, Write to Disk, Config Files | Add .hiero examples and references |
| `src/KernSmith/KernSmith.csproj` | Description, Tags | Add hiero/libgdx mentions |
| `tools/KernSmith.Cli/README.md` | Config flags, init, batch, Config Files | Add .hiero docs |
| `docs/cli/index.md` | Commands table, Config section | Add .hiero references |
| `docs/cli/commands.md` | --config, init, batch | Add .hiero docs |
| `docs/core/index.md` | FromConfig() | Multi-format mention |
| `samples/Program.cs` | Section 2, export | Add .hiero examples |
| `samples/sample.hiero` | N/A | **NEW** sample file |
| `CHANGELOG.md` | Next release | Feature list |

## Estimated Complexity

- **Modified files**: 8–9 documentation files
- **New files**: 1 sample `.hiero` file
- **Risk**: None — documentation only, no code changes
- **Dependencies**: Phases 82–84 must be implemented first (docs describe actual features)
