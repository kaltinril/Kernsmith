# Native determinism audit

Scope: `src/KernSmith.Rasterizers.Native` and `src/KernSmith.Imaging` at base commit 1148282
(Phase 188). Goal: byte-identical atlases and `.fnt` metrics on every host — CLI, GUI,
MonoGame/KNI, Blazor WASM (Mono interpreter and LLVM AOT), NativeAOT, x64 and arm64, .NET 8 and 10.

Legend: **FIXED** = changed in this branch; **EXACT** = reviewed, deterministic as is;
**ORCHESTRATOR** = in a read-only (hinting / core) file, reported not edited.

## 1. Transcendentals (libm-dependent) — 11 call sites, all FIXED

`Math.Pow/Exp/Log/Sin/Cos/Tan/Atan2/Acos/...` call the host C runtime. UCRT (Windows), glibc,
Apple libm and Emscripten's musl-derived libm (browser-wasm) are each accurate to about 1 ulp but
do not round the last bit identically, so a value that then rounds to a byte can land on the
other side of a .5 boundary on another host. The Mono interpreter routes `Math.Sin` etc. to the
C `sin`/`cos`/`pow` of the wasm libc (`MINT_SIN`→`MATH_UNOP(sin)` in `interp.c`), so it is a
fourth libm, not a copy of CoreCLR's.

| File:line (base) | Call | Feeds | Fix |
|---|---|---|---|
| `Internal/Color/Affine2D.cs:21-22` | `Math.Cos/Sin(radians)` | COLR v1 `PaintRotate*` matrices → every pixel of a rotated paint | `DeterministicMath.Cos/Sin` |
| `Internal/Color/Affine2D.cs:31` | `Math.Tan` ×2 | COLR v1 `PaintSkew*` matrices | `DeterministicMath.Tan` |
| `Internal/Color/SrgbTransfer.cs:60` | `Math.Pow(c, 1/2.4)` | sRGB encode thresholds (built once, used for every COLR v1 pixel) | `DeterministicMath.Pow` |
| `Internal/Color/SrgbTransfer.cs:94` | `Math.Pow(.., 2.4)` | sRGB decode table (every CPAL colour in linear light) | `DeterministicMath.Pow` |
| `Internal/Pipeline/ItalicTransform.cs:45` | `Math.Tan(-deg·π/180)` | shear for every non-12° `ItalicAngle` (12° is a fixed-point constant) | `DeterministicMath.Tan` |
| `Internal/Outlines/OutlineStroker.cs:193` | `Math.Acos(dot)` | round-join sweep angle (vector outline pieces) | `DeterministicMath.Acos` |
| `Internal/Outlines/OutlineStroker.cs:211` | `Math.Acos(1 − tol/half)` | round-join step count | `DeterministicMath.Acos` |
| `Internal/Outlines/OutlineStroker.cs:217` | `Math.Atan2(n0)` | round-join start angle | `DeterministicMath.Atan2` |
| `Internal/Outlines/OutlineStroker.cs:221` | `Math.Cos/Sin(angle)` | round-join fan points | `DeterministicMath.Cos/Sin` |
| `Internal/Pipeline/PaintGraphRenderer.cs:467` | `Math.Atan2(dy, dx)` | COLR v1 sweep gradient parameter, per pixel | `DeterministicMath.Atan2` |

`Internal/Raster/GammaCurve.cs` does not exist in this tree (base 1148282). If the orchestrator
carries one over, its `Math.Pow` must go through `DeterministicMath.Pow` (a 256-entry table built
at first use is the natural shape); `NativeSourceDeterminismTests` fails the build of the test
project's run otherwise. `Math.Sqrt`, `MathF.Sqrt` (28 sites), `Floor/Ceiling/Round/Abs/Min/Max/
Clamp/Sign` (hundreds) are IEEE-exact and stay.

Replacement: `src/KernSmith.Rasterizers.Native/Internal/DeterministicMath.cs` — `Exp`, `Log`,
`Pow`, `Sin`, `Cos`, `Tan`, `Atan`, `Atan2`, `Acos` in `double` from `+ − × ÷ Sqrt Round Floor
CopySign` and bit casts only (Cody–Waite range reduction with split constants derived exactly:
π/2 = 0x3FF921FB54400000 + 0x3DD0B4611A400000 + 0x3BE13198A2E03707, ln 2 = 0x3FE62E42FEE00000 +
0x3DEA39EF35793C76; Taylor/Horner series; integer powers by squaring). Accuracy a few double
ulps (≤ 4.5e-15 relative in the tests; narrowed to float never more than one float ulp from
libm). Goldens: all 2131 existing Native tests, every golden included, stayed byte-identical
after the switch — no golden regenerated.

**JIT constant folding:** RyuJIT value-numbers `Math.Sin(const)` etc. using the JIT host's own
libm at compile time, so even a constant-argument transcendental is host-dependent. No call site
had a constant argument, and after the fix none remains.

## 2. SIMD and parallelism

- `Internal/Distance/SignedDistanceField.cs:111-163` `Vector<float>`/`Vector<int>` in
  `SplatColumns` — **EXACT**. Per-lane only: each lane evaluates the scalar expression's
  operations in the scalar order (`+ − × ÷`, compare, select), no cross-lane reduction, no FMA,
  `ConvertToSingle` of column indices below 2^24 is exact. Vector width changes only how many
  columns the vector loop covers before the scalar tail; results are identical per column.
  Pinned by `SdfRendererTests.Compute_VectorColumns_AreBitIdenticalToScalarColumns` (bit compare
  of vector vs `allowVectors:false`). On a host without hardware vectors (`Vector.IsHardwareAccelerated`
  false — wasm without SIMD, some AOT configs) the scalar loop runs, which is the same math.
  Caveat noted: `Vector.Min/Max` NaN semantics differ by ISA on .NET 8, but the kernel uses
  `LessThan`/`GreaterThan` + `ConditionalSelect`, not `Min/Max`, and NaN never enters (segments
  with non-finite coordinates are dropped upstream).
- `Internal/Raster/ScanlineRasterizer.cs:349` comment only — a `Vector<float>` conversion pass
  was measured and not used. No other `System.Numerics`/`Intrinsics` use in Native or Imaging.
- `Parallel.*`, `Task.Run`, `ThreadPool`, `Environment.ProcessorCount`: **none** in Native or
  Imaging (core's `GenerateBatch` parallelism is per-job, each job its own rasterizer instance;
  project_decisions already requires byte-identical serial output).
- `NativeSourceDeterminismTests` now fails on any new `Vector<`/`Vector128..512`/
  `System.Runtime.Intrinsics` outside `SignedDistanceField.cs`, and on any `Parallel`/`Task.Run`/
  `ThreadPool`/`new Thread`/`ProcessorCount` anywhere in the two projects.

## 3. Float semantics

- **Mono interpreter float precision** (browser-wasm interpreted, Android/iOS interpreter):
  **EXACT today.** `src/mono/mono/mini/interp/interp.c` implements `MINT_ADD/SUB/MUL/DIV_R4` as
  `BINOP(float, op)` on C `float` locals and `MINT_CONV_R8_R4` as `(double)(float)`; `MathF.*`
  go to `sinf/cosf/powf` (`MATH_UNOPF`). The historical double-promotion ("float32" option,
  Miguel de Icaza 2018) is gone from the dotnet/runtime interpreter. WebAssembly itself has no
  extended precision: `f32.*`/`f64.*` are exactly rounded, and x87 is not involved on any
  current CoreCLR target (SSE2 baseline on x86/x64).
- **FMA contraction**: RyuJIT never fuses `a*b+c` into FMA on its own — the only JIT attempt
  (dotnet/coreclr PR #25856, "JIT: Recognize FMA patterns") was a do-not-merge demonstration,
  closed unmerged, with the determinism/"no compiler flag to opt in" objections recorded; only
  explicit `Math.FusedMultiplyAdd`/`Fma.*` intrinsics emit FMA. NativeAOT uses the same RyuJIT
  (ILCompiler) and the same policy, on x64 and arm64 alike. Mono LLVM AOT emits LLVM IR without
  `contract` fast-math flags, so LLVM does not fuse either; and WebAssembly's core instruction
  set has no FMA instruction at all (the relaxed-SIMD `relaxed_madd` is opt-in and explicitly
  non-deterministic by spec — dotnet does not emit it). Native uses no `FusedMultiplyAdd` and the
  scan test forbids adding one. Sources: github.com/dotnet/coreclr/pull/25856;
  dotnet/runtime #64604 "Ensure we set -ffp-contract=off on GCC" (the runtime's own native code is
  built without contraction); WebAssembly/simd#79 and sunfishcode "RelaxedMathMode" (FMA not in
  core wasm; relaxed madd nondeterministic).
- **`(int)` casts of out-of-range/NaN floats**: on .NET 8, x86/x64 return `int.MinValue`
  (0x80000000) for NaN/overflow while arm64 saturates (NaN → 0); .NET 9+ made all platforms
  saturating (learn.microsoft.com/dotnet/core/compatibility/jit/9.0/fp-to-integer). So .NET 8
  x64 vs .NET 8 arm64 (and vs .NET 10) can differ **only** for a NaN/±∞/out-of-range cast.
  Reviewed every `(int)`/`(byte)` cast of a float/double in Native (list in
  `grep -rnE "\(int\)" Internal`): all are of `Floor/Ceiling/Round` of finite, bounded values
  (glyph boxes are bounded by `GlyphLimits` at 64 em / 16384 px; coverage, alpha and channel
  values are clamped to 0..255 before or after the cast; `SignedDistanceField.Encode`'s
  `(int)(|d|·128/spread)` is bounded by the clamped range). No NaN can reach a cast: non-finite
  outline coordinates are rejected by `FontValidator`/decoders and `IsFinite` checks in the
  rasterizer. **EXACT**, but worth keeping in mind for new code (a NaN cast is the one place
  .NET 8 x64 and arm64 legitimately disagree).
- **`MathF.Round`/`Math.Round` default (banker's) vs `MidpointRounding.AwayFromZero`**: a semantic
  choice, not a platform dependency — both are exact IEEE operations (`roundsd`/`frintn`, or the
  managed fallback with identical results). Sites: `BitmapResampler.cs:146,176`,
  `ColorLayerRenderer.cs:318`, `BitmapGlyphRenderer.cs:166`, `PaintGraphRenderer.cs:227`,
  `SrgbTransfer.cs:61` (banker's); `GlyphPipeline.cs:126`, `SdfRenderer.cs:36`,
  `AxisNormalizer.cs:75`, `AvarTable.cs:80` (away from zero). **EXACT.**
- **`double`→`float` narrowing**: correctly rounded everywhere (`cvtsd2ss`, `fcvt`,
  `f32.demote_f64`). **EXACT.**
- **Division and `Sqrt`**: IEEE-exact on every target (no `rsqrt`/approximate paths used). **EXACT.**
- **Endianness / table parsing**: `FontReader` uses `BinaryPrimitives.*BigEndian`; Imaging uses
  `BinaryPrimitives.ReadUInt16/32BigEndian`; `SrgbTransfer` uses `BitConverter.SingleToInt32Bits`
  (value-level, endian-free). No `BitConverter.ToInt32(byte[])` or `MemoryMarshal.Cast` over
  multi-byte values. **EXACT** (all current targets are little-endian anyway).
- **Collections**: `Dictionary`/`HashSet` are used only as lookup caches (`_glyphIndexCache`,
  `TableProvider._tables`, `CffDict._entries`, `MvarTable._records`, `GlyphHinter` metric
  caches, `PaintGraphRenderer._prepared`, `CffOutlineDecoder._contexts`, `ColrPaintReader._path`
  cycle guard) — none is iterated into output order. **EXACT.**
- **Culture**: `CffDict.cs:224` parses real operands with `CultureInfo.InvariantCulture`;
  correctly-rounded `double.Parse` since .NET Core 3.0 (shared managed parser on Mono). **EXACT.**
- `Random`, `Stopwatch`, `DateTime`, `Environment.*`: **none** in Native or Imaging.
- **Inflate (Imaging → BCL `ZLibStream`)**: decoding is fully specified by RFC 1951; the output
  bytes do not depend on the zlib build. **EXACT.** (Core's PNG *encoder* output does differ
  between .NET 8 and 10 — zlib-ng — which is why the manifest hashes raw pixels, not PNG files.)
- **`float.Equals`/`==` exact comparisons** (`DropoutControl`, `ScanlineRasterizer`,
  `OutlineFlattener`): comparisons are exact operations; fine given no extended precision.

## 4. Hinting files (read-only) — for the orchestrator

Scanned `Internal/Hinting/*`, `Pipeline/GlyphHinter.cs`, `Pipeline/GlyphPipeline.cs`:

- No transcendental, no SIMD, no parallelism. Math used: `MathF.Abs/Min/Max/Sign/Floor/Round`,
  `Math.Abs/Round/Sqrt/Clamp` — all exact. `GridMath.Round` is `MathF.Floor(v + 0.5f)` (exact).
- `StemFitter.cs:614` `MathF.Round(y[slot] * Resolution) / Resolution` — banker's rounding at
  exact .5 multiples of `1/Resolution`; deterministic, just be aware it is ties-to-even.
- `NormalHinter.cs:121-122` and `GlyphPipeline.cs:126` cast `Math.Round(..., AwayFromZero)` to
  `int` — fine (finite, bounded advances).
- `GlyphHinter.cs:29-30` `Dictionary<CacheKey, ...>` caches — lookup only.
- Requests for the hinting agents: (a) keep using `+ − × ÷`, `Floor/Round/Abs/Min/Max` only; a
  `Math.Pow`/`Exp`/`Log` (e.g. for a stem-darkening curve or a taper) must go through
  `DeterministicMath`; (b) no `Vector<T>`; (c) the manifest's `.../light/*` and `.../normal/*`
  entries (and the hinted goldens) will move when their work lands — regenerate with
  `KERNSMITH_UPDATE_DETERMINISM=1` after merging, never per platform.

## 5. Core (read-only) — for the orchestrator

Not in Native, but on the Native output path:

- `src/KernSmith/Rasterizer/GradientEffect.cs:57-58`, `GradientPostProcessor.cs:91-92`
  `MathF.Cos/Sin(radians)` — libm; affects the gradient *effect* on any backend (not the raw
  glyph). Out of this task's scope; same fix applies if effects are to be byte-identical too.
- `src/KernSmith.Rasterizers.FreeType/FreeTypeRasterizer.cs:737` `Math.Pow` (gamma) — FreeType
  backend only; Native ignores `Gamma`.
- Core's PNG bytes vary with the runtime zlib (documented decision); pixels do not.

## 6. Local verification (Windows 11 x64, .NET 10.0.14 SDK)

- Native test project: 2014/2014 before; after the change the goldens are untouched and the
  suite grows by the new classes (counts in the hand-back report).
- Blazor WASM sample published **interpreted** (`-p:RunAOTCompilation=false`, no wasm-tools),
  served and driven by `wasm-check.mjs` in headless Google Chrome: hash
  `1303f3ed07f73644976f3becf3add04df4f78c2bddbdec9279bc585a4bfcd557` = manifest
  `roboto/32px/grayscale/default` (generated in 137 ms in the browser).
- NativeAOT probe, `win-x64`, `PublishAot=true` (`IlcUseEnvironmentalTools=true` with the VS 18
  MSVC 14.51 linker on PATH — the ILCompiler targets' `vswhere` lookup does not find the VS 18
  installer location from Git Bash): "dynamic code unsupported (AOT)", **182/182 cases match**.
- Not run locally: wasm **AOT** (needs the wasm-tools workload, deliberately not installed) and
  linux-arm64 — both are CI-only.

## 7. What CI now proves

| Leg | Runtime | Proves |
|---|---|---|
| build-and-test windows/ubuntu/macos × net8/net10 (ubuntu now blocking for the Native project) | CoreCLR JIT x64, and arm64 on macos-latest (Apple Silicon) | whole manifest (182 cases) + goldens identical on 3 OS/libm/CPU combinations |
| native-determinism-arm64 (ubuntu-24.04-arm × net8/net10) | CoreCLR JIT linux-arm64 | manifest + goldens + DeterministicMath bits on glibc arm64 (no FreeType oracle there) |
| wasm-publish + wasm-check.mjs | Blazor WASM, Mono LLVM AOT in headless Chromium | manifest case `roboto/32px/grayscale/default` from the browser |
| native-determinism-aot | NativeAOT linux-x64 | 11 manifest cases from an AOT binary (asserts dynamic code is unsupported) |
