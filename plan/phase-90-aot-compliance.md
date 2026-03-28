# Phase 90 — Native AOT Compliance

## Status: Research / Planning

## Goal

Make the KernSmith core library (`src/KernSmith/`) fully Native AOT and trimming compatible, enabling single-file self-contained deployments with no JIT dependency. This unlocks faster startup, smaller binaries, and broader deployment targets (e.g., cloud functions, embedded, WASI).

## Current State Assessment

**The library is NOT AOT-compatible.** No AOT or trimming settings exist in any project file today.

### Issues Found

| # | Severity | File | Issue | Description |
|---|----------|------|-------|-------------|
| 1 | CRITICAL | `Rasterizer/FreeTypeNative.cs` | P/Invoke preservation | 10+ `[DllImport]` declarations for FreeType. Trimmer strips these without explicit roots. |
| 2 | HIGH | `Output/BmFontBinaryFormatter.cs` | Reflection-based JSON | `JsonSerializer.Serialize(dict, JsonOptions)` with `Dictionary<string, object>` — not statically analyzable. |
| 3 | HIGH | `Output/BmFontReader.cs` | Runtime JSON parsing | `JsonDocument.Parse()` with runtime property enumeration (lines 664-676). |
| 4 | MEDIUM | `Output/BmFontModelBuilder.cs` | Assembly reflection | `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` for version extraction (lines 211-213). |
| 5 | LOW | `Rasterizer/FreeTypeRasterizer.cs` | Unsafe interop | Extensive `unsafe`, `GCHandle`, `Marshal` usage — technically AOT-safe if P/Invoke is preserved. |

### What's Already AOT-Safe

- TTF parsing (`Font/` namespace) — pure managed code, no reflection
- Atlas packing (`Atlas/` namespace) — pure managed code
- Output formatters (text/XML) — string building, no reflection
- StbImageSharp / StbImageWriteSharp — pure C# ports, likely AOT-safe
- Platform guards (`RuntimeInformation.IsOSPlatform()`) — AOT-compatible pattern
- Config types, enums, exceptions — plain POCOs

## Research Questions

### R1: FreeTypeSharp AOT Compatibility

- Does FreeTypeSharp 3.1.0 ship with trimming annotations or `IsTrimmable`?
- Do the P/Invoke stubs survive trimming as-is, or do we need a `TrimmerRootDescriptor` XML?
- Since we already have our own `FreeTypeNative.cs` with direct `[DllImport]`, does that simplify things (we control the P/Invoke surface)?
- Should we switch `[DllImport]` to `[LibraryImport]` (source-generated, AOT-native)?

### R2: JSON Serialization Migration

- Can we replace `JsonSerializer.Serialize()` with a `System.Text.Json` source generator (`JsonSerializerContext`)?
- The `Dictionary<string, object>` pattern in `BmFontBinaryFormatter.cs` is the hardest case — can we replace it with a typed model?
- `JsonDocument.Parse()` in `BmFontReader.cs` is read-only DOM access — is this AOT-safe in .NET 10, or does it still need source generators?

### R3: Assembly Reflection Replacement

- Replace `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` with a compile-time constant (MSBuild `<DefineConstants>` or source generator)?
- Or use `[assembly: AssemblyMetadata("Version", "...")]` with a source-generated accessor?

### R4: .NET 10 AOT Improvements

- .NET 10 has significant AOT improvements. Which of our issues are auto-resolved by the target framework?
- Does .NET 10's `JsonDocument` work under AOT without source generators?
- Are there new analyzers we should enable (`EnableAotAnalyzer`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`)?

### R5: FreeTypeSharp Dependency Strategy

- If FreeTypeSharp is not AOT-compatible, options:
  - a) Contribute AOT support upstream
  - b) Fork and add annotations
  - c) Replace with our own minimal FreeType bindings (we already have `FreeTypeNative.cs`)
  - d) Use `[LibraryImport]` source-generated interop for our own bindings

### R6: CLI AOT Publishing

- Once the library is AOT-compatible, can `KernSmith.Cli` publish as a single-file AOT binary?
- What's the expected binary size reduction?
- Does the CLI use any additional non-AOT patterns beyond the library?

## Implementation Plan (Draft)

### Step 1: Enable Analyzers (Low Risk)

Add to `Directory.Build.props`:
```xml
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
```

Build the solution and catalog all warnings. This gives us the authoritative list of issues from the compiler's perspective.

### Step 2: Fix Assembly Reflection

Replace `GetCustomAttribute<>()` in `BmFontModelBuilder.cs` with a compile-time version string. Options:
- `ThisAssembly` source generator
- MSBuild-generated constant
- Simple `const string` updated at build time

### Step 3: Migrate JSON to Source Generators

- Create a `JsonSerializerContext` for `BmFontBinaryFormatter`
- Replace `Dictionary<string, object>` with a typed DTO
- Evaluate whether `JsonDocument.Parse()` in `BmFontReader` needs changes under .NET 10

### Step 4: P/Invoke Modernization

- Evaluate migrating `[DllImport]` to `[LibraryImport]` in `FreeTypeNative.cs`
- `[LibraryImport]` generates marshalling code at compile time — natively AOT-compatible
- Add `[DisableRuntimeMarshalling]` if feasible
- Add `TrimmerRootDescriptor` if FreeTypeSharp needs it

### Step 5: Mark Library as AOT-Compatible

Add to `KernSmith.csproj`:
```xml
<IsAotCompatible>true</IsAotCompatible>
```

This implicitly enables `IsTrimmable` and the trim/AOT analyzers. The build should produce zero warnings at this point.

### Step 6: CLI AOT Publishing

Add to `KernSmith.Cli.csproj`:
```xml
<PublishAot>true</PublishAot>
```

Test single-file AOT publish on Windows, Linux, macOS. Validate all features work correctly.

### Step 7: CI Validation

- Add an AOT publish step to CI that catches regressions
- Optionally add a smoke test that runs the AOT binary

## Estimated Scope

| Step | Effort | Risk |
|------|--------|------|
| 1. Enable analyzers | Small | None — diagnostic only |
| 2. Fix assembly reflection | Small | Low |
| 3. JSON source generators | Medium | Medium — serialization behavior changes |
| 4. P/Invoke modernization | Medium-Large | Medium — native interop changes |
| 5. Mark AOT-compatible | Small | Low — if steps 2-4 are clean |
| 6. CLI AOT publish | Small | Low |
| 7. CI validation | Small | None |

## Dependencies

- .NET 10 SDK (already the target)
- FreeTypeSharp 3.1.0 AOT status (needs research)
- No other phase dependencies

## Risks

- **FreeTypeSharp may not be AOT-compatible** — worst case, we maintain our own FreeType bindings (Step 4 already moves in this direction with `FreeTypeNative.cs`)
- **JSON behavior changes** — source generators can serialize differently than reflection; needs thorough testing
- **Platform-specific native library loading** — AOT binaries need the native `freetype` shared library bundled or available at runtime

## Success Criteria

- [ ] `dotnet build` with AOT/trim analyzers produces zero warnings for `KernSmith`
- [ ] `KernSmith.csproj` has `<IsAotCompatible>true</IsAotCompatible>`
- [ ] `dotnet publish -r win-x64 --self-contained /p:PublishAot=true` succeeds for CLI
- [ ] AOT-published CLI passes the same functional tests as the JIT version
- [ ] CI enforces AOT compatibility on every build
