# Phase 84 — Hiero CLI Changes

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phase 82
> **Goal**: Update the KernSmith CLI tool to support `.hiero` configuration files alongside `.bmfc`.

---

## Implementation Blockers

Phase 84 requires Phase 82's `ConfigFormatFactory` (with `ReadConfig` / `WriteConfig`, public and tested) to be merged first. `BmfcParser.cs` and `BmfcWriter.cs` dispatch through that factory; without it, the CLI cannot auto-detect `.hiero` vs `.bmfc`.

## Current State

The CLI tool has several `.bmfc`-specific code paths:

- `tools/KernSmith.Cli/Config/BmfcParser.cs` — Calls `BmfcConfigReader.Read()` directly (~line 18); switch to `ConfigFormatFactory.ReadConfig()`
- `tools/KernSmith.Cli/Config/BmfcWriter.cs` — Calls `BmfcConfigWriter.WriteToFile()` directly (~line 19); switch to `ConfigFormatFactory.WriteConfig()`
- `tools/KernSmith.Cli/Commands/GenerateCommand.cs` — `BmfcParser.Parse()` call (~line 36); **no change needed** (the factory dispatches inside the parser)
- `tools/KernSmith.Cli/Commands/InitCommand.cs` — Hardcoded `.bmfc` extension appending (~lines 34–36)
- `tools/KernSmith.Cli/Commands/BatchCommand.cs` — Glob/help (~lines 60–110), error message (~line 114), help text (~lines 271–272)
- `tools/KernSmith.Cli/Program.cs` — Help text mentions `.bmfc` (~lines 45, 56–57, 108–109)

## Changes Required

### 1. Config Parser — Format Auto-Detection

**File:** `BmfcParser.cs`

Current:
```csharp
public static CliOptions Parse(string filePath)
{
    var config = BmfcConfigReader.Read(filePath);
    // ... map to CliOptions
}
```

Updated — use the library's `ConfigFormatFactory`:
```csharp
public static CliOptions Parse(string filePath)
{
    var config = ConfigFormatFactory.ReadConfig(filePath);  // auto-detects .hiero vs .bmfc
    // ... map to CliOptions (unchanged)
}
```

Consider renaming class from `BmfcParser` to `ConfigParser` (or keep for backward compatibility).

### 2. Config Writer — Format Auto-Detection

**File:** `BmfcWriter.cs`

Current:
```csharp
public static void Write(CliOptions options, string filePath)
{
    var config = MapToBmfcConfig(options);
    BmfcConfigWriter.WriteToFile(config, filePath);
}
```

Updated:
```csharp
public static void Write(CliOptions options, string filePath)
{
    var config = MapToBmfcConfig(options);
    ConfigFormatFactory.WriteConfig(config, filePath);  // auto-detects from extension
}
```

### 3. Generate Command — No Flag Changes Needed

**File:** `GenerateCommand.cs`

The `--config <path>` and `--save-config <path>` flags pass file paths that include extensions. Since the parser/writer now auto-detect format from extension, no flag changes are needed.

Usage becomes:
```bash
kernsmith generate --config game.hiero --font "Arial" --size 32
kernsmith generate --config game.bmfc
kernsmith generate --save-config output.hiero --font "Arial" --size 32
```

### 4. Init Command — Remove Hardcoded Extension

**File:** `InitCommand.cs` (~lines 34–36)

Current:
```csharp
if (!outputPath.EndsWith(".bmfc", StringComparison.OrdinalIgnoreCase))
    outputPath += ".bmfc";
```

Updated — support both extensions:
```csharp
// Intentional: default to .bmfc for backward compatibility when no recognized
// extension is given. Users opt into .hiero by passing the .hiero extension explicitly.
if (!outputPath.EndsWith(".bmfc", StringComparison.OrdinalIgnoreCase) &&
    !outputPath.EndsWith(".hiero", StringComparison.OrdinalIgnoreCase))
    outputPath += ".bmfc";  // default to .bmfc if no recognized extension
```

**Rationale:** Keep defaulting to `.bmfc` when no extension is supplied (backward compat). Users opt into `.hiero` by giving the `.hiero` extension explicitly. This is intentional — note the comment in the code.

Or add a `--format` flag:
```bash
kernsmith init -o myfont.hiero --font "Arial" --size 32    # extension detected
kernsmith init -o myfont --format hiero --font "Arial"      # explicit format
kernsmith init -o myfont --font "Arial"                     # defaults to .bmfc
```

**Recommendation**: Use extension-based detection (consistent with Phase 82's `ConfigFormatFactory` approach). The `--format` flag is optional and can be added later if needed.

### 5. Batch Command — Mixed Format Support

**File:** `BatchCommand.cs`

**Glob expansion (~lines 60–110):**

Current: Expands `*.bmfc` patterns.

Updated: Also expand `*.hiero` patterns. When the user passes glob patterns, both extensions should be matched:
```bash
kernsmith batch configs/*.bmfc configs/*.hiero --parallel 4
kernsmith batch configs/*.bmfc                           # existing behavior
```

**Directory scanning is DEFERRED:** users supply glob patterns (`configs/*.bmfc configs/*.hiero`). Bare-directory scanning (e.g. `kernsmith batch configs/`) is a future phase — `batch` only accepts file paths and glob patterns for now.

**Error message (~line 114):** Generalize from `"No .bmfc config files specified"` to `"No .bmfc or .hiero config files specified"`.

**Help text (~lines 271–272):** Update references from `.bmfc` to `.bmfc/.hiero`.

### 6. Help Text Updates

**File:** `Program.cs` (~lines 45, 56–57, 108–109)

Update general help text to mention both formats:
```
Config files: .bmfc (BMFont) and .hiero (Hiero/libGDX)
```

**File:** `GenerateCommand.cs` — Flag descriptions:
```
--config <path>       Load settings from a .bmfc or .hiero config file
--save-config <path>  Save settings to a .bmfc or .hiero config file
```

## Files Changed

| File | Change Type | Complexity |
|------|-------------|------------|
| `Config/BmfcParser.cs` | Modified — use `ConfigFormatFactory` | Low |
| `Config/BmfcWriter.cs` | Modified — use `ConfigFormatFactory` | Low |
| `Commands/InitCommand.cs` | Modified — support both extensions | Low |
| `Commands/BatchCommand.cs` | Modified — glob + help text | Low |
| `Commands/GenerateCommand.cs` | Modified — help text only | Trivial |
| `Program.cs` | Modified — help text | Trivial |

## CLI Examples

```bash
# Generate from Hiero config
kernsmith generate --config game.hiero

# Generate from BMFont config (existing, unchanged)
kernsmith generate --config game.bmfc

# Create new Hiero config
kernsmith init -o myfont.hiero --font "Arial" --size 32 --chars "A-Z,a-z,0-9"

# Save current settings as Hiero
kernsmith generate --font "Arial" --size 48 --save-config export.hiero

# Batch process mixed formats
kernsmith batch configs/*.bmfc configs/*.hiero --parallel 4

# Convert between formats (save current .bmfc settings as .hiero)
kernsmith generate --config existing.bmfc --save-config converted.hiero
```

## Test Plan

| Test | Description |
|------|-------------|
| Load via `--config file.hiero` | Loads .hiero, generates font correctly |
| `--config game.bmfc` | Existing behavior unchanged |
| Save via `--save-config out.hiero` | Writes valid .hiero file |
| `--save-config out.bmfc` | Existing behavior unchanged |
| `init -o file.hiero` honors extension | Creates a `.hiero` config file (extension respected) |
| `init -o file` (no ext) defaults to `.bmfc` | Backward-compat default applied |
| `batch *.hiero` | Processes all .hiero files |
| Batch with mixed glob processes both | `batch configs/*.bmfc configs/*.hiero` handles `.bmfc` and `.hiero` together |
| Format conversion | `--config a.bmfc --save-config b.hiero` round-trips |

## Estimated Complexity

- **Modified code**: ~30–40 lines across 4–5 files
- **Risk**: Low — all changes are additive, existing `.bmfc` behavior unchanged
- **Dependencies**: Phase 82 (`ConfigFormatFactory` must exist)
