# Phase 97: Rasterizer Auto-Discovery

**Status:** Complete
**Date:** 2026-04-05

## Problem

Rasterizer backend packages (e.g., `KernSmith.Rasterizers.FreeType`) use `[ModuleInitializer]` to auto-register with `RasterizerFactory`. However, .NET only triggers module initializers when the assembly is actually loaded by the CLR, and the CLR only loads an assembly when code references a type from it.

In practice, consuming projects reference the rasterizer NuGet package but never directly use any type from it — they only use types from the core `KernSmith` package (`BmFont`, `FontGeneratorOptions`, etc.). This means:

1. The rasterizer DLL is copied to the output directory but never loaded.
2. The `[ModuleInitializer]` never fires.
3. `RasterizerFactory.Create()` throws: _"Rasterizer backend 'FreeType' is not registered. No backends have been registered."_

### Who is affected

- **End users** calling `BmFont.Generate()`, `BmFont.Builder().Build()`, or `BmFont.FromConfig()` — the primary public API.
- **Sample project** (`samples/KernSmith.Samples/`) — currently broken out of the box.
- **Gum integration users** calling `BmFont.Builder()` directly for advanced effects (gradient, shadow, etc.) that `BmfcSave` doesn't support.

### Current workarounds (all bad)

Every project that uses KernSmith works around this manually — **13 files** across the codebase:

| Project | Workaround |
|---------|-----------|
| `apps/KernSmith.Ui/Program.cs` | ~10 lines of `RuntimeHelpers.RunModuleConstructor()` for all 4 backends (Gdi/DirectWrite in `#if` guards) |
| `tools/KernSmith.Cli/Program.cs` | Same `RuntimeHelpers.RunModuleConstructor()` pattern for all 4 backends |
| `integrations/KernSmith.GumCommon/GumFontGenerator.cs` | `EnsureFreeTypeRegistered()` with `RuntimeHelpers.RunModuleConstructor()` |
| `samples/KernSmith.Samples.BlazorWasm/Program.cs` | `RuntimeHelpers.RunClassConstructor()` for StbTrueType |
| `tests/KernSmith.Tests/TestAssemblyInitializer.cs` | `RuntimeHelpers.RunModuleConstructor()` for FreeType |
| `tests/KernSmith.Tests/GumFontGeneratorTests.cs` | `RuntimeHelpers.RunClassConstructor()` + manual `RasterizerFactory.Register()` |
| `tests/KernSmith.Tests/Rasterizer/StbTrueTypeRasterizerTests.cs` | `EnsureStbTrueTypeRegistered()` + manual `RasterizerFactory.Register()` |
| `tests/KernSmith.Tests/Rasterizer/GdiRasterizerTests.cs` | `EnsureGdiRegistered()` + manual `RasterizerFactory.Register()` |
| `tests/KernSmith.Tests/Rasterizer/DirectWriteRasterizerTests.cs` | `EnsureDirectWriteRegistered()` + manual `RasterizerFactory.Register()` |
| `tests/KernSmith.Tests/Rasterizer/RasterizerFactoryTests.cs` | `ResetForTesting()` + assert `Create()` throws; `FreeTypeRegistration.Register()` in `finally` cleanup |
| `tests/KernSmith.Tests/Integration/KernSmithFontCreatorTests.cs` | Conditional `RasterizerFactory.Register()` for FreeType |
| `tests/KernSmith.Tests/Integration/EndToEndTests.cs` | Conditional `RasterizerFactory.Register()` for FreeType |
| `tests/KernSmith.Tests/Integration/CombinedBatchTests.cs` | Conditional `RasterizerFactory.Register()` for FreeType |

None of these are acceptable for a library that promises "install the NuGet package and it works."

## Solution

Add auto-discovery to `RasterizerFactory` using `Type.GetType()` with assembly-qualified names, followed by explicit `RuntimeHelpers.RunModuleConstructor()` to guarantee the module initializer fires.

This approach:

- **Works on all platforms** (desktop, Android, iOS, Blazor WASM, consoles) because it goes through .NET's assembly resolution, which knows how to find assemblies in APKs, WASM bundles, etc.
- **Does not use filesystem scanning** (`Directory.GetFiles()` / `Assembly.LoadFrom()`) which would fail on Android (APK-bundled), iOS (AOT-only), Blazor WASM (no filesystem), and consoles (restricted filesystem).
- **Returns null safely** if the assembly isn't referenced by the project — no exceptions.
- **Guarantees `[ModuleInitializer]` execution** via explicit `RunModuleConstructor()` after type resolution (not relying on runtime implementation details).
- **Uses simple assembly names** (no version/culture/public key token) so any compatible version of the rasterizer package is discovered.
- **Does not change any public API** — `Create()`, `Register()`, `GetAvailableBackends()`, `IsRegistered()` all keep their existing signatures and behavior. Users can still select backends via `FontGeneratorOptions.Backend`, the builder's `.WithBackend()`, or CLI flags. Manual `Register()` calls still work and override auto-discovered backends.

### Implementation

Changes are split into two areas: the **core library** (`RasterizerFactory`) and the **rasterizer packages** (trimming protection).

---

### Part A: Core Library Changes (`RasterizerFactory`)

#### A1. Add discovery method

Add a private method that attempts to load known rasterizer assemblies via `Type.GetType()`, then explicitly triggers their module initializers:

```csharp
// In RasterizerFactory.cs

using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

// Assembly-qualified names intentionally omit Version/Culture/PublicKeyToken
// so that any compatible version of the rasterizer package will be discovered.
private static readonly (RasterizerBackend Backend, string TypeName)[] KnownBackends =
[
    (RasterizerBackend.FreeType,
     "KernSmith.Rasterizers.FreeType.FreeTypeRegistration, KernSmith.Rasterizers.FreeType"),
    (RasterizerBackend.StbTrueType,
     "KernSmith.Rasterizers.StbTrueType.StbTrueTypeRegistration, KernSmith.Rasterizers.StbTrueType"),
    (RasterizerBackend.Gdi,
     "KernSmith.Rasterizers.Gdi.GdiRegistration, KernSmith.Rasterizers.Gdi"),
    (RasterizerBackend.DirectWrite,
     "KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRegistration, KernSmith.Rasterizers.DirectWrite.TerraFX"),
];

private static int _discoveryAttempted; // 0 = not attempted, 1 = attempted

[UnconditionalSuppressMessage("Trimming", "IL2057",
    Justification = "Discovery is best-effort; returns null for missing assemblies. " +
                    "Rasterizer packages protect their own types via ILLink.Descriptors.xml.")]
private static void DiscoverBackends()
{
    if (Interlocked.CompareExchange(ref _discoveryAttempted, 1, 0) != 0) return;

    foreach (var (_, typeName) in KnownBackends)
    {
        try
        {
            // Type.GetType loads the assembly if it's reachable.
            // Returns null if the assembly isn't referenced — no exception.
            var type = Type.GetType(typeName);
            if (type != null)
            {
                // Explicitly trigger [ModuleInitializer] — Type.GetType() loading
                // the assembly does not guarantee the module initializer has run.
                RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
            }
        }
        catch (Exception)
        {
            // Assembly found but unloadable (corrupt, wrong arch, missing native dep).
            // Silently skip — the backend simply won't be available.
        }
    }
}
```

#### A2. Hook discovery into all public query methods

Discovery must trigger from `Create()`, `GetAvailableBackends()`, and `IsRegistered()` — not just `Create()`. User code and tests call `GetAvailableBackends()` or `IsRegistered()` directly without going through `Create()`, and those calls must also trigger discovery.

```csharp
public static IRasterizer Create(RasterizerBackend backend)
{
    if (Backends.TryGetValue(backend, out var factory))
        return factory();

    // Assembly may not have been loaded yet — try auto-discovery
    DiscoverBackends();

    if (Backends.TryGetValue(backend, out factory))
        return factory();

    // Still not found — throw with helpful message
    var available = GetAvailableBackends();
    // ... existing error message ...
}

public static IReadOnlyList<RasterizerBackend> GetAvailableBackends()
{
    DiscoverBackends();
    return Backends.Keys.ToList();
}

public static bool IsRegistered(RasterizerBackend backend)
{
    if (Backends.ContainsKey(backend))
        return true;

    DiscoverBackends();
    return Backends.ContainsKey(backend);
}
```

#### A3. Update `ResetForTesting()` to reset discovery state

```csharp
internal static void ResetForTesting()
{
    Backends.Clear();
    Interlocked.Exchange(ref _discoveryAttempted, 0);
}
```

---

### Part B: Rasterizer Package Changes (AOT/Trimming Protection)

The IL trimmer removes types not statically referenced. Since the registration types are only reached via string-based `Type.GetType()`, the trimmer can't see them. Each rasterizer package must protect its own registration type.

**Important:** `[DynamicDependency]` attributes do NOT go on the core library. They only work within the assembly that references the target types. The core library has no reference to any rasterizer package, so `[DynamicDependency]` on `DiscoverBackends()` would be silently ignored by the trimmer.

Instead, each rasterizer package adds an `ILLink.Descriptors.xml` embedded resource to preserve its registration type under trimming/AOT. The descriptor format (using `preserve="all"`) is the correct schema for type preservation; `ILLink.Descriptors.xml` is a different format used for adding attribute annotations.

#### B1. Add `ILLink.Descriptors.xml` to each rasterizer package

**`src/KernSmith.Rasterizers.FreeType/ILLink.Descriptors.xml`:**
```xml
<linker>
  <assembly fullname="KernSmith.Rasterizers.FreeType">
    <type fullname="KernSmith.Rasterizers.FreeType.FreeTypeRegistration" preserve="all" />
  </assembly>
</linker>
```

**`src/KernSmith.Rasterizers.StbTrueType/ILLink.Descriptors.xml`:**
```xml
<linker>
  <assembly fullname="KernSmith.Rasterizers.StbTrueType">
    <type fullname="KernSmith.Rasterizers.StbTrueType.StbTrueTypeRegistration" preserve="all" />
  </assembly>
</linker>
```

**`src/KernSmith.Rasterizers.Gdi/ILLink.Descriptors.xml`:**
```xml
<linker>
  <assembly fullname="KernSmith.Rasterizers.Gdi">
    <type fullname="KernSmith.Rasterizers.Gdi.GdiRegistration" preserve="all" />
  </assembly>
</linker>
```

**`src/KernSmith.Rasterizers.DirectWrite.TerraFX/ILLink.Descriptors.xml`:**
```xml
<linker>
  <assembly fullname="KernSmith.Rasterizers.DirectWrite.TerraFX">
    <type fullname="KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRegistration" preserve="all" />
  </assembly>
</linker>
```

#### B2. Embed the XML in each rasterizer `.csproj`

Add to each rasterizer's `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="ILLink.Descriptors.xml">
    <LogicalName>ILLink.Descriptors.xml</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

This ensures the trimmer preserves the `[ModuleInitializer]` registration type even under aggressive trimming/NativeAOT. The XML is embedded in the NuGet package and automatically consumed by the trimmer when the package is referenced. The `EmbeddedResource` with `LogicalName` approach (rather than `<TrimmerRootDescriptor>`) is required because `TrimmerRootDescriptor` only works at build time for the project that contains it — it does not flow to consumers via NuGet.

---

### Part C: Remove Manual Workarounds

After the fix, remove the manual registration boilerplate from all 13 files:

**Apps & tools:**
- [x] `apps/KernSmith.Ui/Program.cs` — remove `RuntimeHelpers` lines 4-13
- [x] `tools/KernSmith.Cli/Program.cs` — remove `RuntimeHelpers` boilerplate

**Integrations:**
- [x] `integrations/KernSmith.GumCommon/GumFontGenerator.cs` — remove `EnsureFreeTypeRegistered()` method and its call in `Generate()`

**Samples:**
- [x] `samples/KernSmith.Samples.BlazorWasm/Program.cs` — remove `RuntimeHelpers.RunClassConstructor()` for StbTrueType

**Tests (safe removals — no `ResetForTesting()` involved):**
- [x] `tests/KernSmith.Tests/TestAssemblyInitializer.cs` — delete entire file (only purpose was FreeType registration)
- [x] `tests/KernSmith.Tests/GumFontGeneratorTests.cs` — remove registration from constructor, keep `Dispose()`
- [x] `tests/KernSmith.Tests/Integration/KernSmithFontCreatorTests.cs` — remove registration from constructor, keep `Dispose()`
- [x] `tests/KernSmith.Tests/Integration/EndToEndTests.cs` — remove constructor entirely
- [x] `tests/KernSmith.Tests/Integration/CombinedBatchTests.cs` — remove constructor entirely

**Tests (require rework — contain `ResetForTesting()` + "assert Create throws" tests):**
- [x] `tests/KernSmith.Tests/Rasterizer/StbTrueTypeRasterizerTests.cs` — remove `EnsureStbTrueTypeRegistered()` helper AND all call sites (`Factory_Create_StbTrueType_ReturnsStbTrueTypeRasterizer`, `BmFont_Generate_WithStbTrueType_ProducesValidOutput`); **rewrite** `Factory_ResetForTesting_RemovesStbTrueTypeRegistration` (see Part D)
- [x] `tests/KernSmith.Tests/Rasterizer/GdiRasterizerTests.cs` — remove `EnsureGdiRegistered()` helper AND all call sites; **rewrite** `Factory_ResetForTesting_RemovesGdiRegistration` (see Part D). Note: file is inside `#if WINDOWS` guard.
- [x] `tests/KernSmith.Tests/Rasterizer/DirectWriteRasterizerTests.cs` — remove `EnsureDirectWriteRegistered()` helper AND all call sites; **rewrite** `Factory_ResetForTesting_RemovesDirectWriteRegistration` (see Part D). Note: file is inside `#if DIRECTWRITE` guard.
- [x] `tests/KernSmith.Tests/Rasterizer/RasterizerFactoryTests.cs` — **rewrite** `Create_UnregisteredBackend_ThrowsInvalidOperationException` (see Part D)

---

### Part D: Rewrite Broken Tests

Four tests follow the pattern "call `ResetForTesting()`, then assert `Create()` throws `InvalidOperationException`." With auto-discovery, `ResetForTesting()` resets `_discoveryAttempted`, so the next `Create()` re-discovers all backends whose assemblies are loaded in the test process. Since the test project references all 4 backends, `Create()` succeeds instead of throwing.

**Strategy:** These tests verified that `ResetForTesting()` clears the backends dictionary. That behavior still works, but it's now unobservable through the public API — every public method (`Create()`, `GetAvailableBackends()`, `IsRegistered()`) triggers discovery before returning. The tests should be rewritten to verify the new behavior: reset clears backends, then the next call re-discovers successfully.

**Rewrite approach:**

1. **For `StbTrueTypeRasterizerTests`** — verify reset + re-discovery:

```csharp
[Fact]
public void Factory_ResetForTesting_ClearsAndRediscovers()
{
    try
    {
        RasterizerFactory.ResetForTesting();
        // Create() triggers re-discovery after reset and succeeds
        var rasterizer = RasterizerFactory.Create(RasterizerBackend.StbTrueType);
        rasterizer.ShouldBeOfType<StbTrueTypeRasterizer>();
    }
    finally
    {
        RasterizerFactory.ResetForTesting();
    }
}
```

2. **For `GdiRasterizerTests`** (inside `#if WINDOWS`):

```csharp
[Fact]
public void Factory_ResetForTesting_ClearsAndRediscovers()
{
    try
    {
        RasterizerFactory.ResetForTesting();
        var rasterizer = RasterizerFactory.Create(RasterizerBackend.Gdi);
        rasterizer.ShouldBeOfType<GdiRasterizer>();
    }
    finally
    {
        RasterizerFactory.ResetForTesting();
    }
}
```

3. **For `DirectWriteRasterizerTests`** (inside `#if DIRECTWRITE`):

```csharp
[Fact]
public void Factory_ResetForTesting_ClearsAndRediscovers()
{
    try
    {
        RasterizerFactory.ResetForTesting();
        var rasterizer = RasterizerFactory.Create(RasterizerBackend.DirectWrite);
        rasterizer.ShouldBeOfType<DirectWriteRasterizer>();
    }
    finally
    {
        RasterizerFactory.ResetForTesting();
    }
}
```

4. **For `RasterizerFactoryTests.Create_UnregisteredBackend_ThrowsInvalidOperationException`**: Test with a `RasterizerBackend` enum value that has no matching assembly in the `KnownBackends` list. Casting an arbitrary int to an enum is valid C# — `Create()` will attempt discovery, find nothing for value 999, and throw:

```csharp
[Fact]
public void Create_UnregisteredBackend_ThrowsInvalidOperationException()
{
    // Use a backend value with no known assembly — auto-discovery can't find it
    var ex = Should.Throw<InvalidOperationException>(
        () => RasterizerFactory.Create((RasterizerBackend)999));
    ex.Message.ShouldContain("is not registered");
}
```

---

### Part E: Update sample to verify it works

`samples/KernSmith.Samples/Program.cs` should work without modification after this fix — that's the acceptance test.

## Design decisions from validation

### Why `RuntimeHelpers.RunModuleConstructor()` after `Type.GetType()`?

`Type.GetType()` loads the assembly and resolves the type, but the `[ModuleInitializer]` firing on assembly load is a CoreCLR implementation detail, not a documented guarantee. Explicitly calling `RunModuleConstructor()` makes module initializer execution guaranteed across all runtimes.

### Why `Interlocked.CompareExchange` instead of a plain `bool`?

`RasterizerFactory` uses `ConcurrentDictionary` internally, indicating it's designed for concurrent access. A plain `bool` has a harmless but sloppy race condition. `Interlocked.CompareExchange` is trivial and correct.

### Why `try/catch` around `Type.GetType()`?

`Type.GetType()` can throw `FileLoadException` or `BadImageFormatException` if the assembly is found but is corrupt or has a mismatched architecture. Silently skipping is the correct behavior — the backend simply won't be available.

### Why `[UnconditionalSuppressMessage]` instead of `[DynamicDependency]` on the core library?

`[DynamicDependency]` attributes on the core library's `DiscoverBackends()` method would reference assemblies the core library doesn't depend on. The IL trimmer silently ignores `[DynamicDependency]` for assemblies not in the trimming closure. The attributes provide a false sense of security. Instead:
- The core library uses `[UnconditionalSuppressMessage]` to acknowledge the string-based `Type.GetType()` is intentional and best-effort.
- Each rasterizer package protects its own registration type via `ILLink.Descriptors.xml` — the correct place for trimming annotations.

### Why `ILLink.Descriptors.xml` in each rasterizer package?

The IL trimmer and NativeAOT compiler remove types not statically referenced. The `[ModuleInitializer]` registration types are `internal` and never directly referenced by consuming code — they exist solely to be triggered by the CLR on assembly load. Without trimmer protection, these types get removed and auto-discovery's `Type.GetType()` returns null even though the package is referenced. Each package owns its own trimming annotations because:
- Only the package knows which types must be preserved.
- The annotations ship with the NuGet package and are automatically consumed by the trimmer.
- `KernSmith.Rasterizers.StbTrueType` already declares `<IsTrimmable>true</IsTrimmable>` and `<IsAotCompatible>true</IsAotCompatible>`, making this especially important.

### Why simple assembly names (no version)?

.NET Core/5+ assembly binding is version-tolerant by default. Omitting version/culture/public key token means any compatible version of the rasterizer package will be discovered. Including a version would break when users update the rasterizer package without updating the core library.

### Why hook discovery into `GetAvailableBackends()` and `IsRegistered()` too?

User code and tests call these methods directly to check what's available. If discovery only ran from `Create()`, calling `GetAvailableBackends()` before any `Create()` call would return an empty list even though rasterizer packages are installed. The `Interlocked` guard ensures discovery runs at most once regardless of which method triggers it first.

### Does auto-discovery prevent manual backend selection?

No. Auto-discovery only handles *registration* — making backends available in `RasterizerFactory`. Backend *selection* (which backend to use for a given generation) is unchanged:
- `FontGeneratorOptions.Backend` still controls which backend is used
- `BmFontBuilder.WithBackend()` still works
- CLI `--backend` flag still works
- UI rasterizer dropdown still works
- Manual `RasterizerFactory.Register()` still works and overrides auto-discovered backends

## Testing

- [x] `KernSmith.Samples` runs successfully without any manual registration
- [x] Existing tests continue to pass (manual registrations removed, broken tests rewritten) — 1,721 tests pass across all TFMs
- [x] Verify `Type.GetType()` returns null (not an exception) when a rasterizer package is not referenced — verified by .NET documentation and code review; can't unit test in existing project since it references all 4 backends
- [x] Add a unit test for auto-discovery that validates backends are discovered — covered by rewritten `Factory_ResetForTesting_ClearsAndRediscovers` tests
- [x] Add a unit test: `ResetForTesting()` then `Create()` triggers re-discovery — `Factory_ResetForTesting_ClearsAndRediscovers` in StbTrueType/Gdi/DirectWrite test files
- [x] Add a unit test: `Create((RasterizerBackend)999)` throws `InvalidOperationException` — rewritten in `RasterizerFactoryTests`
- [x] Add a unit test: `GetAvailableBackends()` triggers discovery without prior `Create()` call — `GetAvailableBackends_AfterReset_TriggersDiscovery` in `RasterizerFactoryTests`
- [x] Add a unit test: concurrent calls to `Create()` during discovery don't throw — `Create_ConcurrentCallsDuringDiscovery_DoNotThrow` in `RasterizerFactoryTests`; race condition fixed with double-checked locking
- [ ] Verify `KernSmith.Samples.BlazorWasm` still works (StbTrueType auto-discovered) — requires `dotnet publish` runtime verification

## Scope

This phase fixes auto-discovery and adds trimming protection. It does NOT:

- Add new effects to the Gum integration's `BmfcSave` mapping
- Change any public API signatures
- Modify rasterizer implementations beyond adding `ILLink.Descriptors.xml`
- Change how `[ModuleInitializer]` works in the backend packages (those stay as-is)

## Files to modify

### Core library
| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/RasterizerFactory.cs` | Add `DiscoverBackends()`, update `Create()`, `GetAvailableBackends()`, `IsRegistered()`, `ResetForTesting()` |

### Rasterizer packages (new files)
| File | Change |
|------|--------|
| `src/KernSmith.Rasterizers.FreeType/ILLink.Descriptors.xml` | New — trimmer protection for `FreeTypeRegistration` |
| `src/KernSmith.Rasterizers.FreeType/KernSmith.Rasterizers.FreeType.csproj` | Add `EmbeddedResource` for `ILLink.Descriptors.xml` |
| `src/KernSmith.Rasterizers.StbTrueType/ILLink.Descriptors.xml` | New — trimmer protection for `StbTrueTypeRegistration` |
| `src/KernSmith.Rasterizers.StbTrueType/KernSmith.Rasterizers.StbTrueType.csproj` | Add `EmbeddedResource` for `ILLink.Descriptors.xml` |
| `src/KernSmith.Rasterizers.Gdi/ILLink.Descriptors.xml` | New — trimmer protection for `GdiRegistration` |
| `src/KernSmith.Rasterizers.Gdi/KernSmith.Rasterizers.Gdi.csproj` | Add `EmbeddedResource` for `ILLink.Descriptors.xml` |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/ILLink.Descriptors.xml` | New — trimmer protection for `DirectWriteRegistration` |
| `src/KernSmith.Rasterizers.DirectWrite.TerraFX/KernSmith.Rasterizers.DirectWrite.TerraFX.csproj` | Add `EmbeddedResource` for `ILLink.Descriptors.xml` |

### Workaround removals
| File | Change |
|------|--------|
| `apps/KernSmith.Ui/Program.cs` | Remove manual `RuntimeHelpers` boilerplate |
| `tools/KernSmith.Cli/Program.cs` | Remove manual `RuntimeHelpers` boilerplate |
| `integrations/KernSmith.GumCommon/GumFontGenerator.cs` | Remove `EnsureFreeTypeRegistered()` method and call |
| `samples/KernSmith.Samples.BlazorWasm/Program.cs` | Remove manual `RuntimeHelpers` for StbTrueType |

### Test cleanups
| File | Change |
|------|--------|
| `tests/KernSmith.Tests/TestAssemblyInitializer.cs` | Delete entire file |
| `tests/KernSmith.Tests/GumFontGeneratorTests.cs` | Remove registration from constructor |
| `tests/KernSmith.Tests/Integration/KernSmithFontCreatorTests.cs` | Remove registration from constructor |
| `tests/KernSmith.Tests/Integration/EndToEndTests.cs` | Remove constructor entirely |
| `tests/KernSmith.Tests/Integration/CombinedBatchTests.cs` | Remove constructor entirely |

### Test rewrites
| File | Change |
|------|--------|
| `tests/KernSmith.Tests/Rasterizer/StbTrueTypeRasterizerTests.cs` | Remove helper; rewrite reset test to verify re-discovery |
| `tests/KernSmith.Tests/Rasterizer/GdiRasterizerTests.cs` | Remove helper; rewrite reset test to verify re-discovery |
| `tests/KernSmith.Tests/Rasterizer/DirectWriteRasterizerTests.cs` | Remove helper; rewrite reset test to verify re-discovery |
| `tests/KernSmith.Tests/Rasterizer/RasterizerFactoryTests.cs` | Rewrite unregistered backend test to use invalid enum value |

---

## Implementation Note: Reflection-Based Re-Discovery

During implementation, a design gap was discovered: `RuntimeHelpers.RunModuleConstructor()` is **one-shot per process** — it won't re-fire after `ResetForTesting()` clears registrations. The implemented `DiscoverBackends()` addresses this by calling each registration type's `Register()` method via reflection as the primary path, falling back to `RunModuleConstructor()` only if no `Register()` method is found. This makes re-discovery after `ResetForTesting()` reliable in tests.

---

## Part F: Remaining Documentation & Cleanup

Post-implementation validation found documentation, code comments, and plan docs that reference the old manual registration pattern. These need updating to reflect auto-discovery.

### F1. Code comment updates

| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/RasterizerFactory.cs` | Update class-level XML doc to mention auto-discovery alongside manual `Register()` |
| `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRegistration.cs` | Remove redundant `[DynamicDependency]` attribute — `ILLink.Descriptors.xml` now handles trimmer protection |

### F2. User-facing documentation updates

| File | Section | Change |
|------|---------|--------|
| `README.md` | Blazor WASM example (~line 69-90) | Remove `RuntimeHelpers.RunClassConstructor()` snippet or clarify it's only needed for trimmed WASM publishing, not for registration |
| `docs/rasterizers/custom-backend.md` | Registration section (~line 81-144) | Replace manual `RuntimeHelpers.RunModuleConstructor()` guidance with auto-discovery explanation; only show manual pattern for custom third-party backends |
| `docs/rasterizers/index.md` | Auto-Registration section (~line 30-32) | Add note that auto-discovery triggers on first `Create()`/`GetAvailableBackends()`/`IsRegistered()` call |
| `samples/KernSmith.Rasterizer.Example/README.md` | Step 7 (~line 108-117) | Mark manual `RuntimeHelpers` call as optional; explain auto-discovery handles built-in backends |

### F3. Plan doc updates

| File | Change |
|------|--------|
| `plan/master-plan.md` | Add Phase 97 to completed phases list |
| `plan/phase-97-rasterizer-auto-discovery.md` | Move to `plan/done/` when all parts complete |

### F4. Checklist

- [x] Update `RasterizerFactory.cs` class-level XML doc
- [x] Remove `[DynamicDependency]` from `StbTrueTypeRegistration.cs`
- [x] Update `README.md` Blazor WASM section
- [x] Update `docs/rasterizers/custom-backend.md` registration guidance
- [x] Update `docs/rasterizers/index.md` auto-registration section
- [x] Update `samples/KernSmith.Rasterizer.Example/README.md` Step 7
- [ ] Add Phase 97 to `plan/master-plan.md` completed list
- [ ] Move phase-97 plan to `plan/done/`
