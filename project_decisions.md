# Project Decisions

Decisions made, with the why — consult before re-litigating anything. One per line: decided X (not Y) because Z.

## Architecture

- Rasterization uses FreeTypeSharp (MIT, wrapping FreeType via P/Invoke) and uses everything it exposes — metrics, kern-table kerning, glyph bitmaps: industry-standard, small native footprint, supports SDF/hinting/AA modes.
- TTF tables are parsed by our own pure C# parser, covering only what FreeTypeSharp cannot reach (GPOS kerning pairs, OS/2, name strings, variable-font axes). No duplication, no extra dependencies.
- Texture packing is MaxRects (BestShortSideFit) primary with Skyline as a fast mode: MaxRects packs ~93-97% efficiently, Skyline is 2-5× faster for 2-5% less efficiency. Own implementation from public-domain reference code.
- The API is in-memory-model-first with output methods on top (`.ToString()`, `.ToXml()`, `.ToBinary()`, `.ToFile()`), so the core pipeline stays format-agnostic and does zero disk I/O by default.
- MIT throughout, with no paid or split-license dependencies — SixLabors is explicitly excluded for its split license.
- FreeType memory is managed manually via `IDisposable`, pinning font data with `GCHandle`; do NOT use `FreeTypeFaceFacade`.
- Errors use a custom exception hierarchy rooted at `BmFontException`, so a consumer can catch everything this library throws with one clause.
- The desktop UI is MonoGame (DesktopGL) + GUM, code-only (no XAML, no GUM editor). Avalonia, WPF and MAUI were evaluated and rejected because MonoGame+GUM aligns with the game-developer audience. The original rationale also counted on KNI (an API-compatible MonoGame fork) for a cheap Blazor WebGL path later; re-check that assumption before relying on it, since the Gum integrations now live in Vic's repo.
- Cross-platform reach is wherever .NET plus FreeType natives run; FreeTypeSharp still has no Linux ARM64 build, and WASM/browser is covered by the pure-C# StbTrueType backend instead.

## Channels & Compositing

- Per-channel content routing and channel *packing* are two separate mechanisms, deliberately gated differently — conflating them is the easy mistake.
- A non-default `ChannelConfig` is honored **whenever present** (`ShouldApplyChannelConfig` is just `Channels is { IsDefault: false }`), because `ChannelCompositor` natively routes outline/shadow content into individual channels whether or not other effects are active. The narrower 0.15.2 gate — apply only when `fourChnlPacked=1` or the font has no baked effects — no longer exists.
- `ChannelPacking` (grayscale-per-glyph packing via `ChannelPackedAtlasBuilder`) stays mutually exclusive with both effects and color fonts, enforced by hard `InvalidOperationException` guards in `Generate`: effects produce RGBA glyphs and color glyphs are RGBA, neither of which can be packed into a single channel.

## Float Equality

- Exact `==` on floats is kept at every CodeQL-flagged site (not an epsilon), for four distinct reasons. An epsilon would be a bug at most of them, not a fix.
- *Bit-copy*: `MathF.Max` selects one of its arguments rather than computing a new value, so `max == rf` is an identity test; an epsilon could match two near-equal channels and pick the wrong hue sector.
- *Divide-by-zero guard*: `delta == 0` guards `x / delta`, and IEEE gives exactly `0.0` for `x - x`, so a true gray always trips it; an epsilon would flatten faintly-colored pixels to gray.
- *Fast-path skip*: `(1*x)+(0*y)` is exactly `x` and `(t - 0f)/1f` is exactly `t`, so taking the branch or skipping it is output-identical — these cannot produce a wrong answer at all.
- *Default round-trip*: `if (options.SdfSpread != 8f)` asks "did the user change this field", not "is this equal to 8"; an epsilon would silently fail to persist a user-set 8.0001, which is data loss.
- `y0 == y1` horizontal-edge rejection stays exact because the fill relies on signed Δy summing to zero over a closed contour — dropping an epsilon-Δy edge would break closure and produce fill artifacts. `OutlineFlattenerTests` asserts exactly this invariant.
- The genuinely risky shape — comparing two independently-computed values, e.g. `computedAdvance * scale == target` — does not appear in this codebase; that is why the alerts were dismissed individually rather than filtering the rule.

## CodeQL Configuration

- Filter by `problem.severity: recommendation`, never by rule id, so nothing CodeQL rates error or warning is ever hidden. Dropping the filter restores the style-suggestion noise that previously buried real findings.
- No `paths-ignore` block in the config (removed in PR #197): it is inert for C# when the workflow builds, and dead config that looks load-bearing is worse than no config.
- Generated-code alerts (`RegexGenerator.g.cs`) are dismissed "won't fix" rather than filtered — the finding is real (an unread `timeout` local) but the file is emitted by Microsoft's regex source generator on every build.
- The rasterizer float-equality alerts stay OPEN deliberately, as a standing prompt to re-check if that code changes.
- If generated-code noise ever grows, the real lever is the build step in `codeql.yml`, not the config file.

## Regression Harness

- `regression_check.py` builds `GenerateAll` as a separate **fatal** step, because `dotnet run` exits 1 for both a build failure and a partial generation; tolerating both let a non-compiling harness leave stale output in place, so the diff compared main against itself and reported a false pass.
- A backend declining a capability the config requests (SDF on GDI/DirectWrite/Native) is a **skip**, not a failure — otherwise adding an SDF config would permanently red the harness.
- An `sdf.bmfc` config exists specifically because no other config exercised the SDF path, so the harness would report "identical" for SDF changes without ever running them.

## Ownership & Dependencies

- All four Gum integration projects were removed from this repo (2026-07-18) and Vic took over every Gum integration; core `KernSmith` stays Jeremy's. This also fixed a UI startup crash, since `Gum.Themes.Editor.MonoGame` was compiled against Gum's own `KernSmith.MonoGameGum` build while the local ProjectReference won restore.
- Gum family packages are bumped in **lockstep**, never one at a time: merging Dependabot PRs individually caused a GumCommon/Gum.MonoGame version split that broke `main`.

## Tooling

- xUnit + Shouldly, never FluentAssertions — paid licensing (Phase 79).
- No ReadyToRun: benchmarked ~15% slower than plain JIT on .NET 10.
