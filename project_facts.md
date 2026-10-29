# Project Facts

Source of truth for statements about the project. One short fact per line. Update in place; delete facts that become false. No status counts or inventories that drift — record what is load-bearing.

## Identity

- Brand is stylized **KernSmith** (capital S).
- The GitHub repo is canonically `kaltinril/Kernsmith` (lowercase s) — per the API `full_name` and the git remote — so the brand and the repo name genuinely disagree.
- Jeremy's GitHub username: `kaltinril`. Vic = `vchelaru` (Gum author).
- Version source of truth is `Directory.Build.props` `<Version>`; all projects inherit it, and no csproj carries its own `PackageVersion`.
- `publish.yml` fires on a pushed `v*` tag **and** on `workflow_dispatch` — unlike some sibling repos, tagging here really does publish.
- License: MIT (see `LICENSE`).

## Stack & Conventions

- C# / .NET, multi-targeting `net8.0;net10.0`, so building the solution needs both SDKs.
- Projects are deliberately **not** unified on one TFM. `Directory.Build.props` sets the `net8.0;net10.0` default, used by core, `KernSmith.Fonts.Web`, and the FreeType/StbTrueType/Native backends. The exceptions: `Rasterizers.Gdi` is `net8.0-windows;net10.0-windows`; `Rasterizers.DirectWrite.TerraFX` is `net10.0-windows` only; `KernSmith.Cli` adds both `-windows` variants; `KernSmith.Ui` is `net10.0;net10.0-windows`; `Samples.BlazorWasm` is `net10.0` only.
- NuGet package ID and main API class are both `KernSmith`.
- Nullable reference types are enabled.
- Testing is xUnit + Shouldly. `dotnet test` runs in Microsoft.Testing.Platform (MTP) mode — `global.json` sets `"test": {"runner": "Microsoft.Testing.Platform"}` because xunit.v3 4.x dropped VSTest mode on the .NET 10 SDK. Consequences: pass projects as `dotnet test --project <csproj>` (or `--solution`), TRX comes from `--report-xunit-trx [--report-xunit-trx-filename x.trx] --results-directory <dir>` (the VSTest `--logger`/`--blame-hang` flags no longer exist), and MTP writes results to `TestResults/` under the output folder unless `--results-directory` is given.
- Vic's Gum integration packages (`KernSmith.GumCommon`, `KernSmith.MonoGameGum`) are compiled against a specific published KernSmith version (see the nuspec dependency). The `<Version>` in `Directory.Build.props` must be **>=** that version, or the Gum tests in `tests/KernSmith.Tests` fail at runtime with `FileNotFoundException: Could not load ... KernSmith, Version=X` — restore and build do not catch this because the local ProjectReference wins over the package dependency.
- Core library dependencies: StbImageSharp, StbImageWriteSharp. FreeTypeSharp belongs to `KernSmith.Rasterizers.FreeType`, not the core library.
- `AllowUnsafeBlocks` is set in the `src/KernSmith.Rasterizers.*` backends and the `tests/bmfont-compare/GenerateAll` harness — never in the core library.

## Namespaces

- `KernSmith` (root): entry point, config types, exceptions, enums. Files in `Config/` and `Exceptions/` use this root namespace.
- `KernSmith.Font`: font reading, TTF parsing. `.Models`: FontInfo, KerningPair, GlyphMetrics. `.Tables`: HeadTable, HheaTable, Os2Metrics, NameInfo.
- `KernSmith.Rasterizer` (singular): IRasterizer, post-processors, effects (IGlyphEffect), GlyphCompositor.
- `KernSmith.Rasterizers.*` (plural): rasterizer backend packages.
- `KernSmith.Atlas`: IAtlasPacker, packers, encoders (PNG/TGA/DDS), AtlasBuilder, AtlasSizeEstimator, ChannelCompositor.
- `KernSmith.Output`: formatters, FileWriter, BmFontResult, BmFontReader, BmFontModelBuilder. `.Model`: BmFontModel, InfoBlock, CommonBlock, ExtendedMetadata.

## Repo & Git

- `/tests/bmfont-compare/` is gitignored wholesale; a handful of generic-font `.bmfc` configs there are tracked as force-added exceptions, and the rest reference machine-specific Gum fonts and are intentionally untracked.
- A new `.bmfc` added with plain `git add` is silently dropped: `git status` does not even list it as untracked, so it looks committed but never reaches the repo.

## Ownership & Dependencies

- All four Gum integration projects (GumCommon, KniGum, MonoGameGum, FnaGum) were removed from this repo; Vic owns them, source lives in his repo under `Integrations/KernSmith/`.
- Core `KernSmith` stays Jeremy's (co-owned on NuGet, but authors/projectUrl remain kaltinril).
- The UI has no local ProjectReference to the Gum integrations — it gets `KernSmith.MonoGameGum` transitively via `Gum.Themes.Editor.MonoGame`.
- Dependabot vulnerability alerts are **enabled** (`GET /repos/kaltinril/Kernsmith/vulnerability-alerts` → 204; 404 would mean disabled).

## Feature State

- `.bmfc` channel config (`alphaChnl`/`redChnl`/`greenChnl`/`blueChnl`) is parsed by `BmfcConfigReader` into `ChannelConfig` and honored on the CLI/core path (shipped 0.15.2, PR #128).
- The desktop UI carries channel config end-to-end (`EffectsViewModel.Channels` → `GenerationRequest.Channels` → `GenerationService` sets `options.Channels`).
- The Native rasterizer renders TrueType (`glyf`) outlines end to end (Phases 161-165), and is exposed in the CLI (`--rasterizer native`) and the README as clearly-labeled experimental. Not published to NuGet (`IsPackable=false`).
- Native today: CFF/OTF faces and bitmap-only color fonts (`CBDT`/`sbix`, no `glyf`) throw `RasterizationException` at `LoadFont`; `Sdf` requests throw `NotSupportedException`; `ColorFont` renders `COLR` v0 layers as straight-alpha RGBA (v1-only glyphs fall back to grayscale); `Bold`/`Italic` and hinting are ignored. TrueType variable fonts render at the requested instance (`SupportsVariableFonts` is true), color layers included; named instances are not selectable — core passes user axis values only. WOFF1 and WOFF2 work because core decompresses them before any backend sees the bytes.
- The FreeType backend's color glyphs are premultiplied RGBA (FreeType's BGRA, channel-swapped), but core treats every `Rgba32` glyph as straight alpha, so FreeType's colored edges come out darker; Native's color glyphs are straight.
- Native's `glyf` decoder draws outlines where they are; FreeType moves a glyph so its `hmtx` left side bearing is honored when it disagrees with the `glyf` xMin (Roboto: 52 glyphs off by one unit; Bungee Color's '4' inline layer off by 350).
- Core never reads `SupportedAntiAliasModes` or `SupportsOutlineStroke`: the requested `AntiAlias` mode is passed straight through, and Native renders every mode other than `None` as grayscale. Outlines are always drawn by core's bitmap `OutlineEffect`, and `RasterOptions` carries no outline width.
- WOFF2 decoding is core-only (`Font/Woff2/`, `Font/Brotli/`): `ManagedBrotliDecoder` is our own RFC 7932 decoder, and its 122,784-byte static dictionary ships as the raw-deflate embedded resource `Font/Brotli/BrotliDictionary.bin` (`*.bin` is marked binary in `.gitattributes`). It is about 6x slower than the BCL native decoder (Roboto WOFF2: ~10 ms vs ~1.7 ms; ~18 ms for the whole WOFF2-to-sfnt decode).
- `KernSmith.Fonts.Web` fetches the `.woff2` source of an `@font-face` rule when offered, else the `.woff` (`CssParser`, by `format()` hint, else extension). CDNs key the format on User-Agent: Google Fonts serves TTF to unknown agents and only WOFF2 to modern browsers, so `WebFontSource` sends a Chrome UA, but not when `OperatingSystem.IsBrowser()` — there the header goes into `fetch()`, is not CORS-safelisted, and Firefox's preflight is rejected by Bunny and Google. The disk cache names files `.woff2`/`.woff` from the byte signature.
- The `.woff2` fixtures (`Roboto-Regular`, `NotoSans-Regular-Latin`, `RobotoFlex-Latin`) were made by fontTools from the `.ttf` committed beside each, so tests can compare a decoded WOFF2 with its exact source; `FONT-LICENSES.md` has the commands.
- Core already supersamples and height-stretches for any backend with `HandlesOwnSizing = false`, Native included (`PreparedRasterization`).
- Native renders through a staged internal pipeline (`Internal/Pipeline/`): `IOutlineDecoder` -> `IGlyphTransform` list (empty) -> `GlyphBox` -> `GlyphHinter` (no-op) -> `IGlyphRenderer` dispatch (coverage; `ColorLayerRenderer` wraps it for `COLR` v0 glyphs); the Native README's "Glyph pipeline" section says where each phase plugs in. `GlyphBox` uses the `glyf` header bbox when the decoder declares one and the outline's tight bounds otherwise; `TrueTypeOutlineDecoder` places outlines by the TrueType left side bearing phantom point (pp1 = xMin - lsb, the `USE_MY_METRICS` component's for such composites — matching FreeType; Roboto has 52 glyphs where lsb != xMin); `NativeOutputCharacterizationTests` pins Native's exact output hashes.
- Color-font fixtures: `NotoColorEmoji.ttf` (CBDT/CBLC only, no COLR) in `tests/KernSmith.Tests/Fixtures/`; `Color/BungeeColor-Regular.ttf` (COLR v0, 9 palettes) and `Color/ReemKufiInk-Regular.ttf` (COLR v1 only) in a subfolder, because core `ColorFontTests` uses the first color font directly in `Fixtures/` and expects the emoji font. There is no committed `.otf` (CFF), CFF2 or variable color fixture. `RobotoFlex-Variable.ttf` carries `fvar`/`avar`/`gvar`/`HVAR`/`MVAR` (its `MVAR` varies no line metric); `DancingScript-Variable.ttf` is single-axis with short `gvar` offsets.

## Build & Test Gotchas

- CliTests and `WoffAllBackendsTests` shell out to the real CLI, resolving `tools/KernSmith.Cli/bin/<configuration>/<tfm>/KernSmith.Cli.dll` from their own output path, so they work under any configuration — but they need the solution built first, which is why CI always runs `dotnet build` before `dotnet test`.
- The regression harness exercises the CLI `GenerateAll` path only, not the UI `GenerationService`; verify UI-generation changes separately.
- `GenerateAll` skips a (config, backend) pair when the backend declines a capability the config requests — e.g. SDF on GDI/DirectWrite/Native. Any other per-pair exception (a CFF font on Native) is caught and logged, and `regression_check.py` carries on, so a backend failing a config never reds the run — a new feature's own test has to assert the backend actually produced output.
- `dotnet run` exits 1 for **both** a build failure and a partial generation, so the two must be distinguished by building as a separate step.
- `tests/KernSmith.PlatformSmoke` is deliberately **not** in `KernSmith.sln`: its mobile TFMs need the android/ios/maccatalyst workloads, which the normal CI legs don't install. It builds `net10.0;net10.0-android`, adding `net10.0-ios;net10.0-maccatalyst` only on macOS; `-p:SmokeTarget=<tfm>` narrows it to one target. Only `.github/workflows/mobile-smoke.yml` and the `run-*.sh` scripts build it, always in Release (trimming/AOT).
- Building the Android leg locally needs Android SDK platform API 36: `dotnet build tests/KernSmith.PlatformSmoke -t:InstallAndroidDependencies -f net10.0-android -p:SmokeTarget=net10.0-android -p:AndroidSdkDirectory=<sdk>`.
- The bundled FreeType's `FT_Fixed` (C `long`) is pointer-sized even on Windows x64, so FreeType P/Invoke arrays of `FT_Fixed` (e.g. variation coordinates) must be `nint`, not `int` — an `int` array is read in pairs and silently scrambles every value.
- Android RID fallback gotcha: NuGet maps android-x64 -> linux-x64, so FreeTypeSharp's desktop glibc `runtimes/linux-x64/libfreetype.so` overrides the Android `.so` from its `.aar` and x86_64 fails with `DllNotFoundException` (`libm.so.6`). `src/KernSmith.Rasterizers.FreeType/buildTransitive/KernSmith.Rasterizers.FreeType.targets` (packed to `build/` and `buildTransitive/`) removes FreeTypeSharp `NativeCopyLocalItems` on Android; project references must `<Import>` it explicitly. With it, every packaged `libfreetype.so` is 16KB-aligned.
- Platform verification: Android (API 34 emulator, x86_64 + arm64, Release, `TrimMode=full`) passes StbTrueType, FreeType and Native. Linux arm64 (CI `ubuntu-24.04-arm`) and Mac Catalyst (CI `macos-26`, maccatalyst-arm64, Release, full AOT) pass StbTrueType and Native; FreeType fails on both with no native library. iOS simulator (CI `macos-26`, iOS 26.4, iossimulator-arm64, Release, no JIT) passes StbTrueType (generate + SDF) and Native; FreeType is not built for iOS (link failure, below). A real-iPhone run (Phase 186) is still pending as extra confirmation. `DefaultSystemFontProvider` returns 0 fonts on Android, iOS and Mac Catalyst (no throw).
- FreeType on iOS: an iOS app that references `KernSmith.Rasterizers.FreeType` fails to **link**, even if FreeType is never used — FreeTypeSharp 3.1.0's static iOS `libfreetype.a` references 20 libpng symbols (FreeType sfnt PNG bitmaps, `Load_SBit_Png`) that nothing provides. Fix needs a libpng for iOS or an upstream FreeTypeSharp change. The smoke app's iOS build drops the FreeType `ProjectReference` and defines `KSMOKE_NO_FREETYPE`.
- Smoke `KSMOKE_EXPECT_FAIL=<backends>`: those backends' failures report `XFAIL` and don't count; a passing `generate` reports `XPASS` and fails the run, so the table can't go stale. CI sets it for FreeType on Linux arm64 and Mac Catalyst (no `continue-on-error`).
- `mobile-smoke.yml` pins the ios/maccatalyst workloads to 26.5.10318 via a rollback file: the newest workload (27.0) requires Xcode 27, which the `macos-26` runners (Xcode 26.x) lack. Bump the pin with the runner image. NativeAOT publish needs a device RID, so there is no simulator AOT leg; full Apple AOT is covered by Mac Catalyst and real devices.
- Local NuGet validation needs `packageSourceMapping`: route `KernSmith*` to the local feed and `*` to nuget.org in the consumer `nuget.config`.

## CodeQL

- The only open alerts are the deliberate `y0 == y1` ones — `ScanlineRasterizer.cs` and `OutlineFlattener.cs`, plus the two `OutlineFlattenerTests` assertions that pin the invariant. Everything else is fixed or dismissed.
- `paths` / `paths-ignore` do **nothing** for C# here: they apply only when CodeQL analyses *without building*, and `codeql.yml` builds the solution explicitly.
- Code-scanning dismissal comments are capped at **280 characters**; longer ones return HTTP 422, which looks like a generic failure if stderr is swallowed.
- A dismissal comment **cannot be edited in place** ("Alert is already dismissed", HTTP 400) — PATCH `state=open` first, then re-dismiss.
- PR-level CodeQL results live on `refs/pull/N/merge`. `refs/pull/N/head` has **no analyses at all**, so querying it returns a meaningless empty set that reads like a pass.

## File Map

| What | Location |
|------|----------|
| Entry point | `src/KernSmith/BmFont.cs` |
| Plan docs | `plan/` (start with `master-plan.md`) |
| Data types (source of truth) | `plan/done/plan-data-types.md` |
| Implementation order | `plan/done/plan-implementation-order.md` |
| Tests | `tests/KernSmith.Tests/` |
| Test font fixture | `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf` |
| Regression/output comparison | `tests/bmfont-compare/regression_check.py` |
| Platform smoke test (desktop/Android/iOS/Mac Catalyst) | `tests/KernSmith.PlatformSmoke/` |
| FreeType Android packaging fix | `src/KernSmith.Rasterizers.FreeType/buildTransitive/KernSmith.Rasterizers.FreeType.targets` |
| CI/CD | `.github/workflows/` |
| Docs site | kaltinril.github.io/Kernsmith |
