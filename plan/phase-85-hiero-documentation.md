# Phase 85 — Hiero Documentation Updates

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phases 82, 83, 84
> **Goal**: Update all documentation, README, samples, and NuGet metadata to reflect `.hiero` format support.

---

## Dependency Gate

Phase 85 work begins **only after Phases 82–84 land** — i.e. `BmFontResult.ToHiero()`, `ConfigFormatFactory`, `HieroConfigReader`/`HieroConfigWriter`, and CLI/UI `.hiero` support are all present and merged. The docs describe shipped behavior, so they must not be written ahead of the features.

## Files Requiring Updates

> **Note on line numbers:** Line numbers below are approximate and may have drifted. Locate the current section by its heading/content rather than relying on the exact line number. File paths are precise.

> **API symbols the docs must reference:** `BmFont.FromConfig` (now auto-detects `.bmfc`/`.hiero` by inspecting file content, with the extension used only as a fallback), `BmFontResult.ToHiero()`, and `ConfigFormatFactory.ReadConfig` / `ConfigFormatFactory.WriteConfig`.

### 1. README.md (Root)

**Feature list section:**
Add `.hiero` (Hiero/libGDX) to supported config formats alongside `.bmfc`.

**Quick Start section:**
Add example showing `.hiero` usage:
```csharp
// From Hiero config (format auto-detected by inspecting file content)
var result = BmFont.FromConfig("myfont.hiero");
```

**ToFile output section:**
Mention that config export is done by the caller via `ToBmfc()`/`ToHiero()` (`.ToFile()` writes `.fnt` + images; it does not take a config path).

**Fluent Builder section:**
Add `FromConfig("base.hiero")` example.

**Write to Disk / In-Memory section:**
Document `ToHiero()` method alongside `ToBmfc()`.

**Reading and Writing config files section:**
Expand section to cover both formats:
```csharp
// Read any supported config format (auto-detected by inspecting file content;
// extension is only a fallback when content is inconclusive)
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

**`<Description>` (~line 10):**
```xml
<Description>BMFont-compatible bitmap font atlases from TTF/OTF/WOFF files. Supports BMFont .bmfc and Hiero .hiero configuration formats.</Description>
```

**`<PackageTags>` (~line 16):**
Add the `hiero` and `libgdx` tags (lowercase, consistent with existing tags):
```xml
<PackageTags>bmfont;bitmap-font;hiero;libgdx;font-atlas;...</PackageTags>
```

### 3. CLI README

**File:** `tools/KernSmith.Cli/README.md`

**Config flags section:**
Update `--config` and `--save-config` descriptions to mention both formats.

**init Command section:**
Add `.hiero` example:
```bash
kernsmith init -o myfont.hiero --font "Arial" --size 32
```

**batch Command section:**
Document mixed-format batch processing:
```bash
kernsmith batch configs/*.bmfc configs/*.hiero --parallel 4
```

**Config Files section:**
Add new subsection documenting `.hiero` format structure with example.

### 4. CLI Docs

**File:** `docs/cli/index.md` (locate the commands table and the Configuration Files section)
- Update command table to mention `.hiero` in init and batch descriptions
- Update Configuration Files section

**File:** `docs/cli/commands.md` (locate the `--config`/`--save-config`, init, and batch sections)
- Update `--config` and `--save-config` flag docs
- Update init command docs
- Update batch command docs

### 5. Core Library Docs

**File:** `docs/core/index.md` (locate the `FromConfig()` description)
- Update `FromConfig()` description: "generate from a `.bmfc` or `.hiero` configuration file (format auto-detected by inspecting file content, with the extension used only as a fallback)"

### 6. Sample Code

**File:** `samples/KernSmith.Samples/Program.cs`

**Section 2 (FromConfig):**
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

**Export section:**
Add `ToHiero()` example:
```csharp
var hieroString = memResult.ToHiero();
Console.WriteLine($"Hiero config: {hieroString.Length} chars");
```

### 7. CHANGELOG.md

Add to the existing **Unreleased** section. The entry scope spans the **library + CLI + UI** (Phases 82–84) — list the library APIs, CLI flag/command changes, and UI dialog/drag-drop changes together:
```markdown
### Added
- Hiero `.hiero` configuration file format support (read and write)
- `HieroConfigReader` and `HieroConfigWriter` classes
- `ConfigFormatFactory` for auto-detecting config format by inspecting file content (read path; write selects by extension)
- `BmFontResult.ToHiero()` for exporting Hiero config strings
- `BmFont.FromConfig()` and `BmFontBuilder.FromConfig()` now auto-detect `.hiero` and `.bmfc` by inspecting file content
- CLI: `--config` and `--save-config` accept `.hiero` files
- CLI: `init` command can create `.hiero` configs
- CLI: `batch` command processes mixed `.bmfc` and `.hiero` files
- UI: Load/Save project dialogs accept `.hiero` files
- UI: Drag-and-drop supports `.hiero` files
```

### 8. Sample .hiero File

Create a sample `.hiero` file for the samples directory. The content **must conform to `reference/REF-10-hiero-format-reference.md`** and should only use fields KernSmith actually maps (cross-check against the Phase 82 mapping). Deferred/unmapped Hiero fields — e.g. `font.gamma`, `pad.advance.x/y` — should be **omitted** (or commented as ignored), since they have no KernSmith equivalent and are deferred to Phase 100. Keep the example faithful to the format syntax (UTF-8, `key=value`, effects as repeating `effect.class` blocks).

**New file:** `samples/KernSmith.Samples/sample.hiero` (created by the Phase 85 implementation, not now)
```
font.name=Arial
font.size=32
font.bold=false
font.italic=false
font.mono=false

font2.file=
font2.use=false

pad.top=1
pad.right=1
pad.bottom=1
pad.left=1

glyph.native.rendering=false
glyph.page.width=512
glyph.page.height=512
glyph.text=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz1234567890 !"#$%&'()*+,-./:;<=>?@[\]^_`{|}~

render_type=2

effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect
effect.Color=ffffff
```
(`font.gamma` and `pad.advance.x/y` are intentionally omitted — they are unmapped/deferred per Phase 82.)

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

---

> **Plan review 2026-03-24**: Updated drifted line number references for README.md (~298+, ~368+), CLI README (~314+, ~433+), and samples/Program.cs (~42, ~113). All file paths and type references verified as accurate.
