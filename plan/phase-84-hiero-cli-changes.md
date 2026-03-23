# Phase 84 — Hiero CLI Changes

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phase 82
> **Goal**: Update the KernSmith CLI tool to support `.hiero` configuration files alongside `.bmfc`.

---

## Current State

The CLI tool has several `.bmfc`-specific code paths:

- `tools/KernSmith.Cli/Config/BmfcParser.cs` — Calls `BmfcConfigReader.Read()` directly
- `tools/KernSmith.Cli/Config/BmfcWriter.cs` — Calls `BmfcConfigWriter.WriteToFile()` directly
- `tools/KernSmith.Cli/Commands/GenerateCommand.cs` — `--config` and `--save-config` flags, no format detection
- `tools/KernSmith.Cli/Commands/InitCommand.cs` — Hardcoded `.bmfc` extension appending
- `tools/KernSmith.Cli/Commands/BatchCommand.cs` — Glob expansion assumes `*.bmfc`, help text references `.bmfc`
- `tools/KernSmith.Cli/Program.cs` — Help text mentions `.bmfc`

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

**File:** `InitCommand.cs` (lines ~35–36)

Current:
```csharp
if (!outputPath.EndsWith(".bmfc", StringComparison.OrdinalIgnoreCase))
    outputPath += ".bmfc";
```

Updated — support both extensions:
```csharp
if (!outputPath.EndsWith(".bmfc", StringComparison.OrdinalIgnoreCase) &&
    !outputPath.EndsWith(".hiero", StringComparison.OrdinalIgnoreCase))
    outputPath += ".bmfc";  // default to .bmfc if no recognized extension
```

Or add a `--format` flag:
```bash
kernsmith init -o myfont.hiero --font "Arial" --size 32    # extension detected
kernsmith init -o myfont --format hiero --font "Arial"      # explicit format
kernsmith init -o myfont --font "Arial"                     # defaults to .bmfc
```

**Recommendation**: Use extension-based detection (consistent with Phase 82's `ConfigFormatFactory` approach). The `--format` flag is optional and can be added later if needed.

### 5. Batch Command — Mixed Format Support

**File:** `BatchCommand.cs`

**Glob expansion (lines ~89–110):**

Current: Expands `*.bmfc` patterns.

Updated: Also expand `*.hiero` patterns. When user passes glob patterns, both extensions should be matched:
```bash
kernsmith batch fonts/*.bmfc fonts/*.hiero --parallel 4
kernsmith batch fonts/*.bmfc                              # existing behavior
kernsmith batch configs/                                  # auto-find .bmfc and .hiero (requires new directory-scanning logic — not currently supported; batch only accepts file paths and glob patterns)
```

**Help text (lines ~266, ~271):** Update references from `.bmfc` to `.bmfc/.hiero`.

### 6. Help Text Updates

**File:** `Program.cs`

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
| `--config game.hiero` | Loads .hiero, generates font correctly |
| `--config game.bmfc` | Existing behavior unchanged |
| `--save-config out.hiero` | Writes valid .hiero file |
| `--save-config out.bmfc` | Existing behavior unchanged |
| `init -o font.hiero` | Creates .hiero config file |
| `init -o font` | Defaults to .bmfc (backward compat) |
| `batch *.hiero` | Processes all .hiero files |
| `batch *.bmfc *.hiero` | Mixed format batch |
| Format conversion | `--config a.bmfc --save-config b.hiero` round-trips |

## Estimated Complexity

- **Modified code**: ~30–40 lines across 4–5 files
- **Risk**: Low — all changes are additive, existing `.bmfc` behavior unchanged
- **Dependencies**: Phase 82 (`ConfigFormatFactory` must exist)
