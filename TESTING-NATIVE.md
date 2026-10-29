# Testing the Native rasterizer before release

The Native rasterizer (`src/KernSmith.Rasterizers.Native`) is experimental and not on NuGet
(`IsPackable=false`). This guide covers trying it from a source checkout in the desktop UI, the
CLI, a MonoGame sample, and your own MonoGame/KNI/Gum projects, without packing or publishing
anything. Run every command from the repository root after `dotnet build KernSmith.sln`.

## Desktop UI

```bash
dotnet run --project apps/KernSmith.Ui -f net10.0-windows   # net10.0 on Linux/macOS
```

Pick **Native (experimental)** in the **Rasterizer** dropdown. Changing the rasterizer regenerates
when Auto-Regenerate is on. Native's options:

| Option | Where |
|--------|-------|
| Anti-alias None / Grayscale / Light / LCD | Effects > Font Style > **Anti-Alias** (lists only the backend's modes) |
| LCD filter and subpixel order | Effects > Font Style > **LCD Filter** / **LCD Order** (enabled in LCD mode) |
| Light auto-hinting | Anti-Alias **Light** with **Hinting** checked (Native hints nothing else) |
| SDF / MSDF / MTSDF | Effects > Advanced > **Distance Field** (MSDF/MTSDF are listed only for Native) |
| SDF scale | Effects > Advanced > **SDF Scale** (1-4, any backend with SDF) |
| Vector outline | Effects > Outline > **Mode** = Vector (enabled only on Native with no distance field) |
| Color glyphs and palette | Effects > Advanced > **Color Font** and **Palette** (enabled for fonts with color tables) |
| Variation axes | Effects > **Variable Font** sliders (appear for fonts with `fvar`) |
| Synthetic bold/italic | Effects > Font Style > **Bold** / **Italic** |
| Synthetic width and italic angle | Effects > Font Style > **Width %** (10-1000) and **Italic Angle** (-60 to 60, enabled with Italic) |

On other rasterizers the Vector mode, **Width %** and **Italic Angle** are disabled and show the
value actually used (Effect, 100, 12); the value you chose comes back when you switch to Native.

The **Preview** tab shows the raw atlas (an MSDF atlas is a red/green/blue field). The **Sample**
tab decodes SDF, MSDF and MTSDF pages back to glyph shapes, so it should show crisp text.
**File > Save Project** writes these settings to the `.bmfc` (`rasterizer=native`, `sdfMode=`,
`sdfScale=`, `antiAlias=`, `lcdFilter=`, `lcdOrder=`, `outlineMode=`, `widthPercent=`,
`italicAngle=`, `colorPalette=`, `variationAxes=`), and loading the project restores them. Saving
as `.hiero` lists the ones Hiero cannot store before writing.

To also render the app's own interface text with Native through the Gum integration
(`KernSmithFontCreator`, see below), launch with `--ui-rasterizer`:

```bash
dotnet run --project apps/KernSmith.Ui -f net10.0-windows -- --ui-rasterizer native
```

Good looks like: menus, labels and buttons look the same as with the default (FreeType).

## CLI

```bash
dotnet tools/KernSmith.Cli/bin/Debug/net10.0/KernSmith.Cli.dll generate -f <font> -s 32 --rasterizer native [options]
```

Native options: `--aa none|grayscale|light|lcd` (`--mono` = `none`), `--hinting`/`--no-hinting`,
`--sdf` or `--sdf-mode sdf|msdf|mtsdf`, `--sdf-spread <n>`, `--color-font`, `--color-palette <n>`,
`--axis <tag>=<value>` (repeatable), `--instance <name>` (a named instance; `--axis` overrides its
values), `-b/--bold`, `-i/--italic`. `kernsmith info <font>` lists a font's axes and named instances.

## MonoGame sample

```bash
dotnet run --project samples/KernSmith.Samples.Minimal -f net10.0 -- --rasterizer native [--font <path>]
```

Draws the generated atlas page (Roboto by default). The window title names the backend.

## Your own MonoGame or KNI game (no Gum)

KernSmith core has no XNA dependency, so MonoGame and KNI work the same way. In the game's
`.csproj`, reference this checkout:

```xml
<ProjectReference Include="<checkout>\src\KernSmith\KernSmith.csproj" />
<ProjectReference Include="<checkout>\src\KernSmith.Rasterizers.Native\KernSmith.Rasterizers.Native.csproj" />
```

Generate with `Backend = RasterizerBackend.Native` and upload each `AtlasPage` with
`page.GetPremultipliedRgbaPixelData()` (for `BlendState.AlphaBlend`). In a trimmed or AOT build
(iOS, Blazor WASM), register the backend explicitly at startup, because auto-discovery uses
reflection: `RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());`.

## Gum on MonoGame or KNI (Vic's integration packages)

`KernSmith.MonoGameGum` / `KernSmith.KniGum` (and `KernSmith.GumCommon`) are NuGet packages built
against a released KernSmith. A local ProjectReference to KernSmith takes precedence over that
package dependency, so no packing is needed:

1. Keep the `KernSmith.MonoGameGum` (or `KernSmith.KniGum`) PackageReference.
2. Add the two ProjectReferences above.
3. Create the font creator on Native, after any Gum theme's `Apply()` (themes install their own creator):

   ```csharp
   CustomSetPropertyOnRenderable.InMemoryFontCreator =
       new KernSmith.Gum.KernSmithFontCreator(GraphicsDevice, RasterizerBackend.Native);
   ```

The `<Version>` in this checkout's `Directory.Build.props` must be at least the KernSmith version
the Gum package depends on (its nuget.org page lists it), or the app fails at run time with
`FileNotFoundException: Could not load ... KernSmith, Version=X`. This repository runs this path
itself: the UI's `--ui-rasterizer native` and `GumNativeIntegrationTests` in
`tests/KernSmith.Ui.Tests`.

### Local NuGet feed (only if ProjectReferences are not an option)

Not done yet, because it means packing. Native is `IsPackable=false`, so a local pack would need
either a temporary, uncommitted `IsPackable=true` in its `.csproj` or the global property on the
command line (expected to override the project's value; untested here). Give the packages a
prerelease version so they cannot collide with a released version in the global packages cache:

```bash
dotnet pack src/KernSmith -c Release -o <feed> -p:Version=0.24.0-local.1
dotnet pack src/KernSmith.Rasterizers.Native -c Release -o <feed> -p:Version=0.24.0-local.1 -p:IsPackable=true
```

In the consumer's `nuget.config`, map the exact KernSmith package IDs to the feed. A
`KernSmith*` pattern would also route Vic's `KernSmith.MonoGameGum`/`GumCommon`/`KniGum` there and
fail restore, since they live on nuget.org:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="kernsmith-local" value="<feed>" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="kernsmith-local">
      <package pattern="KernSmith" />
      <package pattern="KernSmith.Rasterizers.Native" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Then reference `KernSmith` and `KernSmith.Rasterizers.Native` at `0.24.0-local.1` next to the Gum
package. Repacking under the same version needs `dotnet nuget locals global-packages --clear`.

## Checklist

Fixtures are in `tests/KernSmith.Tests/Fixtures/` (color fonts in `Color/`). Compare against
FreeType by switching the rasterizer.

| Try | Font | Good looks like |
|-----|------|-----------------|
| Default grayscale | `Roboto-Regular.ttf` | Same shapes, advances and baseline as FreeType (within a pixel) |
| `.otf` outlines | `SourceSerif4-Regular.otf` (CFF), `NotoSans-Regular-Latin-CFF.otf` | Renders like the TTF fonts; no blank glyphs |
| WOFF2 | `Roboto-Regular.woff2` | Identical to `Roboto-Regular.ttf` |
| Mono | Roboto at 8 px, AA None | Thin strokes (`l I \| . :`) never vanish |
| Light hinting | Roboto at 10-14 px, Light + Hinting | Horizontal stems and x-height snap to whole pixels (crisper than Grayscale); Light without Hinting equals Grayscale |
| LCD | Roboto at 24 px, LCD | RGBA atlas with colored subpixel fringes, normal width (FreeType's LCD is 3x-wide grayscale) |
| LCD filter / order | Roboto at 24 px, LCD, each **LCD Filter** and **LCD Order** | None has the strongest fringes, Smooth the weakest; BGR swaps the red and blue fringes; V-RGB/V-BGR fringe vertically (top/bottom edges) |
| SDF | Roboto, Distance Field SDF | Soft glow in Preview, crisp text in Sample; close to FreeType's SDF |
| SDF scale | Roboto, Distance Field SDF, **SDF Scale** 1 then 4 | Same layout; smoother field and Sample edges at 4 |
| MSDF / MTSDF | Roboto, Distance Field MSDF/MTSDF | Colored field in Preview, sharp corners in Sample; combining with outline, shadow, gradient, fill color, channel packing, super sampling or color font is refused with an error |
| Variable TrueType | `RobotoFlex-Variable.ttf` (wght 100-1000, wdth, opsz, ...), `DancingScript-Variable.ttf` | Glyphs follow the sliders; CLI `--instance Black` equals `--axis wght=900` |
| Variable CFF2 | `SourceCodeVF-Upright.otf` | Weight follows `wght` (the default instance is ExtraLight) |
| COLR v0 | `Color/BungeeColor-Regular.ttf`, Color Font on | Two-tone letters; palettes 0-8 recolor them; palette 9 is an error |
| COLR v1 | `Color/ReemKufiInk-Regular.ttf`, Color Font on, characters ٠١٢٣٤٥٦٧٨٩ (U+0660-0669) | Gradient-filled digits (Latin text stays gray) |
| Bitmap color (CBDT/sbix) | `NotoColorEmoji.ttf`, Color Font on | Emoji in color, like FreeType |
| Synthetic bold / italic | Roboto, Bold / Italic | Same stroke weight and 12° slant as FreeType, nothing clipped |
| Italic angle | Roboto, Italic, **Italic Angle** 30 then -12 | Steeper slant at 30, leaning left at -12, nothing clipped; switching to FreeType disables the box and renders 12° |
| Width % | Roboto, **Width %** 50 then 150 | Glyphs, advances and kerning half / one-and-a-half as wide; switching to FreeType disables the box and renders 100% |
| Vector outline | Roboto at 32 px, Outline width 3, **Mode** Vector vs Effect | Vector has a crisp ring edge and solid narrow counters; Effect looks like FreeType's outline. Picking a distance field switches the combo back to Effect (disabled) |
| Project round trip | Any of the above, Save then Load Project | Rasterizer, anti-alias, LCD filter/order, distance field, SDF scale, outline mode, width, italic angle, palette and axes come back |
| Gum | UI with `--ui-rasterizer native` | UI text renders normally |
