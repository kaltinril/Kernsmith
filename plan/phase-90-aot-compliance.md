# Phase 90 — Native AOT Compliance

## Status: Ready to Implement

## Scope

**In scope:** `src/KernSmith/` (core library) and `src/KernSmith.Rasterizers.StbTrueType/`
**Out of scope:** FreeType, GDI, DirectWrite rasterizer backends — these have P/Invoke-heavy native interop and are separate packages; AOT for them is a future concern.

## Goal

Make the KernSmith core library and StbTrueType rasterizer fully Native AOT and trimming compatible, enabling single-file self-contained deployments with no JIT dependency. This unlocks faster startup, smaller binaries, and broader deployment targets (e.g., cloud functions, embedded, WASI).

## Current State Assessment (Evaluated 2026-04-05)

**~95% compliant.** The core library is almost entirely AOT-safe already. StbTrueType is already marked AOT-compatible. Only two concrete fixes needed.

### Compliance Matrix — Core Library (`src/KernSmith/`)

| Area | Status | Detail |
|------|--------|--------|
| Dynamic code generation | **Compliant** | No `Reflection.Emit`, `Expression.Compile`, `DynamicMethod` |
| P/Invoke declarations | **Compliant** | No `[DllImport]` in core lib (P/Invoke is only in rasterizer backends) |
| AOT safety attributes | **Compliant** | No suppression attributes needed |
| JSON serialization | **Compliant** | `Utf8JsonWriter` (BmFontBinaryFormatter) and `JsonDocument.Parse()` (BmFontReader) are both AOT-safe in .NET 10 |
| TTF parsing | **Compliant** | Pure managed code, no reflection |
| Atlas packing | **Compliant** | Pure managed code |
| Output formatters (text/XML) | **Compliant** | String building, no reflection |
| Config types, enums, exceptions | **Compliant** | Plain POCOs |
| Platform guards | **Compliant** | `RuntimeInformation.IsOSPlatform()` is AOT-compatible |
| Assembly reflection | **Fix needed** | `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` in `BmFontModelBuilder.cs:211-214` |
| AOT/Trim project settings | **Fix needed** | No `IsAotCompatible`, `EnableTrimAnalyzer`, or `EnableAotAnalyzer` in `KernSmith.csproj` |

### Compliance Matrix — StbTrueType (`src/KernSmith.Rasterizers.StbTrueType/`)

| Area | Status | Detail |
|------|--------|--------|
| Project settings | **Compliant** | Already has `IsTrimmable`, `EnableTrimAnalyzer`, `IsAotCompatible` |
| Code patterns | **Compliant** | Uses `GCHandle` and `unsafe` blocks — both AOT-safe |
| Dependencies | **Compliant** | StbTrueTypeSharp is a pure C# port |

### Issues Found

| # | Severity | File | Issue | Description |
|---|----------|------|-------|-------------|
| 1 | HIGH | `Output/BmFontModelBuilder.cs:211-214` | Assembly reflection | `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` for version extraction — not AOT-compatible |
| 2 | MEDIUM | `KernSmith.csproj` | Missing AOT settings | No `IsAotCompatible`, no analyzers enabled — warnings are invisible to developers |

### What Was Evaluated and Found Clean

- **No `JsonSerializer.Serialize()` calls** — BmFontBinaryFormatter uses `Utf8JsonWriter` directly (AOT-safe)
- **`JsonDocument.Parse()` in BmFontReader** — read-only DOM access, AOT-safe in .NET 10
- **`Dictionary<string, object>` in BmFontBinaryFormatter** — used only with `Utf8JsonWriter` manual serialization, not reflection-based `JsonSerializer`; AOT-safe as-is
- **No `Type.GetType()`, `Activator.CreateInstance()`, `Assembly.Load()`, `MakeGenericType()`** — searched entire `src/KernSmith/`
- **No `[DynamicallyAccessedMembers]` needed** — no member-level reflection patterns found
- **StbImageSharp / StbImageWriteSharp** — pure C# ports, AOT-safe

## Research Answers

### R1: FreeTypeSharp AOT Compatibility
**Out of scope** — FreeType rasterizer backend is excluded from this phase.

### R2: JSON Serialization
**No migration needed.** BmFontBinaryFormatter uses `Utf8JsonWriter` directly (not `JsonSerializer`). BmFontReader uses `JsonDocument.Parse()` which is AOT-safe in .NET 10. No `JsonSerializerContext` or source generators required.

### R3: Assembly Reflection Replacement
**One fix needed.** `BmFontModelBuilder.cs:211-214` uses `GetCustomAttribute<AssemblyInformationalVersionAttribute>()`. Replace with a compile-time constant via MSBuild-generated property or `ThisAssembly` source generator.

### R4: .NET 10 AOT Improvements
**Most concerns are auto-resolved.** `JsonDocument` is fully AOT-safe in .NET 10. The new analyzers (`EnableAotAnalyzer`, `EnableTrimAnalyzer`) should be enabled to catch regressions.

### R5: FreeTypeSharp Dependency Strategy
**Out of scope** — deferred to future phase for rasterizer backends.

### R6: CLI AOT Publishing
**Deferred.** CLI depends on rasterizer backends which are out of scope. Once a rasterizer backend is AOT-compatible, CLI can be published as AOT.

## Implementation Plan

### Step 1: Enable Analyzers on Core Library

Add to `KernSmith.csproj`:
```xml
<IsAotCompatible>true</IsAotCompatible>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
```

Build and catalog any warnings. This is diagnostic-only and makes AOT issues visible.

### Step 2: Fix Assembly Reflection

Replace `GetCustomAttribute<>()` in `BmFontModelBuilder.cs:211-214` with a compile-time version string. Options:
- MSBuild-generated `<DefineConstants>` or `<AssemblyAttribute>`
- Simple `const string` populated by build
- `ThisAssembly` source generator (adds a dependency)

Preferred: MSBuild-generated constant (zero new dependencies).

### Step 3: Build Verification

- Build core library and StbTrueType with analyzers enabled
- Confirm zero AOT/trim warnings
- Run existing test suite to confirm no regressions

### Step 4 (Future): CLI AOT Publishing

Deferred until a rasterizer backend is also AOT-compatible. When ready:
```xml
<!-- KernSmith.Cli.csproj -->
<PublishAot>true</PublishAot>
```

## Estimated Scope

| Step | Effort | Risk |
|------|--------|------|
| 1. Enable analyzers | Small | None — may surface unexpected warnings |
| 2. Fix assembly reflection | Small | Low |
| 3. Build verification | Small | None |

**Total effort: Small.** This is a ~1 hour phase.

## Dependencies

- .NET 10 SDK (already the target)
- No other phase dependencies

## Risks

- **Unexpected analyzer warnings** — enabling analyzers may flag issues not caught in manual review. Mitigated by doing step 1 first as diagnostic.
- **StbImageSharp/StbImageWriteSharp** — assumed AOT-safe as pure C# ports; verify with analyzer output.

## Success Criteria

- [ ] `KernSmith.csproj` has `<IsAotCompatible>true</IsAotCompatible>`
- [ ] `dotnet build` with AOT/trim analyzers produces zero warnings for `KernSmith` and `KernSmith.Rasterizers.StbTrueType`
- [ ] Assembly reflection in `BmFontModelBuilder.cs` replaced with compile-time constant
- [x] `KernSmith.Rasterizers.StbTrueType` marked AOT-compatible (already done)
- [x] No dynamic code generation patterns in core lib (verified clean)
- [x] No P/Invoke in core lib (verified — only in out-of-scope rasterizer backends)
- [x] JSON serialization is AOT-safe (`Utf8JsonWriter` + `JsonDocument.Parse()`, no `JsonSerializer`)
- [x] No problematic reflection patterns beyond `BmFontModelBuilder.cs:211`
- [ ] Existing test suite passes with no regressions
