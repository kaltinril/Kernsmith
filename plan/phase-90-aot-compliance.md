# Phase 90 — Native AOT Compliance

## Status: Complete — implemented 2026-05-30 (Option A for Issue #3; build clean, 930 tests pass)

## Scope

**In scope:** `src/KernSmith/` (core library) and `src/KernSmith.Rasterizers.StbTrueType/`
**Out of scope:** FreeType, GDI, DirectWrite rasterizer backends — these have P/Invoke-heavy native interop and are separate packages; AOT for them is a future concern.

## Goal

Make the KernSmith core library and StbTrueType rasterizer fully Native AOT and trimming compatible, enabling single-file self-contained deployments with no JIT dependency. This unlocks faster startup, smaller binaries, and broader deployment targets (e.g., cloud functions, embedded, WASI).

## Current State Assessment (Evaluated 2026-04-05)

**Mostly compliant, with one significant exception.** Most of the core library is AOT-safe and StbTrueType is already marked AOT-compatible. However, `RasterizerFactory` (core lib) uses reflection-based type resolution AND loaded-assembly scanning, which are AOT/trim-incompatible. Three areas need work: (1) the assembly version reflection, (2) project AOT/trim settings, and (3) `RasterizerFactory` reflection/assembly scanning (requires a design decision — see Issue #3).

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
| Assembly version reflection | **Fix needed** | `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` in `BmFontModelBuilder.cs` (`BuildExtendedMetadata`, ~line 211-214) |
| Rasterizer auto-discovery | **Fix needed** | `RasterizerFactory.DiscoverBackends()` resolves optional backend assemblies by string name: `Type.GetType(typeName)` then `type.GetMethod("Register", ...)` + `MethodInfo.Invoke` (fallback `RuntimeHelpers.RunModuleConstructor`). Triggers IL2057/IL2075 under trim/AOT analyzers (Phase 97 feature). See Issue #3. |
| AOT/Trim project settings | **Fix needed** | No `IsAotCompatible`, `EnableTrimAnalyzer`, or `EnableAotAnalyzer` in `KernSmith.csproj` (note: `TargetFrameworks net8.0;net10.0` is inherited from `Directory.Build.props`, not set in the csproj) |

### Compliance Matrix — StbTrueType (`src/KernSmith.Rasterizers.StbTrueType/`)

| Area | Status | Detail |
|------|--------|--------|
| Project settings | **Compliant** | Already has `IsTrimmable`, `EnableTrimAnalyzer`, `IsAotCompatible` |
| Code patterns | **Compliant** | Uses `GCHandle` and `unsafe` blocks — both AOT-safe |
| Dependencies | **Compliant** | StbTrueTypeSharp is a pure C# port |

### Issues Found

| # | Severity | File | Issue | Description |
|---|----------|------|-------|-------------|
| 1 | HIGH | `Output/BmFontModelBuilder.cs` (~211-214) | Assembly version reflection | `GetCustomAttribute<AssemblyInformationalVersionAttribute>()` for version extraction — not AOT-compatible |
| 2 | MEDIUM | `KernSmith.csproj` | Missing AOT settings | No `IsAotCompatible`, no analyzers enabled — warnings are invisible to developers |
| 3 | HIGH | `Rasterizer/RasterizerFactory.cs` | Reflection-based auto-discovery | `DiscoverBackends()` uses `Type.GetType(typeName)` + `GetMethod`/`Invoke` (and `RunModuleConstructor`) to load optional backend assemblies by name. Triggers IL2057/IL2075 once trim/AOT analyzers are enabled. Note: the file already carried local `[UnconditionalSuppressMessage]` for IL2057/IL2075 before this phase. Resolved via Step 2b Option A. |

### What Was Evaluated and Found Clean

- **No `JsonSerializer.Serialize()` calls** — BmFontBinaryFormatter uses `Utf8JsonWriter` directly (AOT-safe)
- **`JsonDocument.Parse()` in BmFontReader** — read-only DOM access, AOT-safe in .NET 10
- **`Dictionary<string, object>` in BmFontBinaryFormatter** — used only with `Utf8JsonWriter` manual serialization, not reflection-based `JsonSerializer`; AOT-safe as-is
- **No `Reflection.Emit`, `Expression.Compile`, `DynamicMethod`, `Assembly.Load()`, `MakeGenericType()`** — searched entire `src/KernSmith/`
- **CORRECTION:** `Type.GetType()` IS used — in `RasterizerFactory.DiscoverBackends()` (plus `GetMethod`/`Invoke` and `RunModuleConstructor`). `Activator.CreateInstance()` is NOT used. This reflection is tracked as Issue #3. (An earlier revision of this doc incorrectly listed `Type.GetType` as absent and incorrectly named methods `TryCreate`/`ScanLoadedAssemblies`, which do not exist.)
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

### Step 2: Fix Assembly Version Reflection

Replace `GetCustomAttribute<>()` in `BmFontModelBuilder.cs` (`BuildExtendedMetadata`, ~line 211-214) with a compile-time version string. Options:
- MSBuild-generated `<DefineConstants>` or `<AssemblyAttribute>`
- Simple `const string` populated by build
- `ThisAssembly` source generator (adds a dependency)

Preferred: MSBuild-generated constant (zero new dependencies).

### Step 2b: Resolve RasterizerFactory Reflection (DECISION REQUIRED)

`RasterizerFactory.DiscoverBackends()` (Phase 97 auto-discovery) uses `Type.GetType` + reflection invocation that the trim/AOT analyzers flag (IL2057/IL2075). The method already carried local `[UnconditionalSuppressMessage]` attributes for IL2057/IL2075. Approaches considered:

- **Option A — Local suppression (CHOSEN):** Keep `[UnconditionalSuppressMessage]` on the private `DiscoverBackends` only, extended to cover the AOT analyzer code (IL3050) the newly-enabled analyzer surfaces. Contains the warning at the single reflection site, keeps the public API (`BmFont`, `BmFontBuilder`) clean, and still achieves zero warnings. Auto-discovery keeps working under JIT; trim/AOT consumers use explicit `Register(...)`. Matches the code's pre-existing local-suppression strategy.
- **Option B — Annotate and propagate:** Put `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` on the public `Create`/`GetAvailableBackends`/`IsRegistered`. Rejected: these attributes propagate the warning to ALL callers — including `BmFont`, the library entry point, plus the CLI, UI, and tests — cascading the "not AOT-safe" label across the entire public API.
- **Option C — Explicit registration only for AOT:** Document that AOT/trim consumers must call `RasterizerFactory.Register(...)`; exclude auto-discovery from the AOT-compatible surface. (This is effectively the runtime behavior under Option A.)

**Decision: Option A** — minimal change, contains the warning, keeps the public API AOT-clean, consistent with the code's existing local-suppression approach.

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
| 2. Fix assembly version reflection | Small | Low |
| 2b. Resolve RasterizerFactory reflection (Option A, local suppression) | Small | Low — contained to one private method; public API unchanged |
| 3. Build verification | Small | None |

**Total effort: Small–Medium.** Steps 1, 2, 3 are ~1 hour; Step 2b adds a design decision and possible public-API annotation work.

## Dependencies

- .NET 10 SDK (already the target)
- No other phase dependencies

## Risks

- **Unexpected analyzer warnings** — enabling analyzers may flag issues not caught in manual review. Mitigated by doing step 1 first as diagnostic.
- **StbImageSharp/StbImageWriteSharp** — assumed AOT-safe as pure C# ports; verify with analyzer output.

## Success Criteria

- [x] `KernSmith.csproj` has `<IsAotCompatible>true</IsAotCompatible>` (+ `EnableTrimAnalyzer`, `EnableAotAnalyzer`)
- [x] `dotnet build` with AOT/trim analyzers produces zero warnings for `KernSmith` and `KernSmith.Rasterizers.StbTrueType`
- [x] Assembly version reflection in `BmFontModelBuilder.cs` replaced with compile-time constant (MSBuild-generated `KernSmithVersionInfo.Version`, zero new deps)
- [x] `RasterizerFactory.DiscoverBackends()` reflection resolved per Step 2b (Option A: local `[UnconditionalSuppressMessage]` for IL2057/IL2075/IL3050, public API kept clean)
- [x] `KernSmith.Rasterizers.StbTrueType` marked AOT-compatible (already done)
- [x] No dynamic code generation patterns in core lib (no `Reflection.Emit`/`Expression.Compile`/`DynamicMethod` — verified clean)
- [x] No P/Invoke in core lib (verified — only in out-of-scope rasterizer backends)
- [x] JSON serialization is AOT-safe (`Utf8JsonWriter` + `JsonDocument.Parse()`, no `JsonSerializer`)
- [x] Existing test suite passes with no regressions (930 passed, 0 failed)
