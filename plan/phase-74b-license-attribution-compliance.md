# Phase 74b: Third-Party License Attribution & Compliance

> **Status**: Complete
> **Created**: 2026-06-03
> **Completed**: 2026-06-03
> **Follow-up to**: phase-74-mit-license

## Purpose

KernSmith is MIT-licensed, but it redistributes (or links against) several
third-party components whose licenses carry attribution obligations that the
repository does not currently satisfy. This phase brings the project into full,
honorable compliance with every dependency license — the way a careful
corporate / open-source project would — by adding a canonical
`THIRD-PARTY-NOTICES.md`, attributing FreeType-derived source, packing the
notices into the NuGet packages, and documenting the licenses of bundled
runtime natives and test fonts.

A license-compliance review (2026-06-03) found three real gaps, all centered on
**FreeType**, plus several "good-hygiene" improvements. This phase closes them.

## Findings being addressed

1. **FreeType FTL credit line is missing.** `KernSmith.Rasterizers.FreeType`
   ships FreeType native binaries (via the FreeTypeSharp package), and the
   CLI / UI copy those natives into their publish output. The FreeType License
   (FTL) requires a credit line in product documentation. Nothing in the repo
   credits FreeType today.
2. **FreeType-derived source code is uncredited.**
   `src/KernSmith.Rasterizers.StbTrueType/OutlineTransforms.cs` is, by its own
   comments, a *"faithful port of FreeType's `FT_Outline_EmboldenXY`."* That
   makes it FreeType-derived expression living inside an otherwise
   public-domain (stb) package, with no FreeType attribution.
3. **No consolidated `THIRD-PARTY-NOTICES` file.** NuGet delivers each
   dependency's own license transitively to *package* consumers, but the
   shipped CLI/UI binaries redistribute FreeType (FTL), MonoGame (Ms-PL),
   SDL2 (Zlib), and **OpenAL Soft (LGPL)** with no bundled notices.

Plus good-hygiene items: bundled MonoGame natives (SDL2/OpenAL/FAudio), the
KNI (Ms-PL) and Gum (MIT) integrations, NativeFileDialogNET (Zlib), and the
test fonts committed to the repo (Roboto Apache-2.0; Noto Color Emoji /
Roboto Flex / Dancing Script OFL) are undocumented.

## Decisions (recorded)

- **D1 — FreeType license election: the FreeType License (FTL).** FreeType is
  dual-licensed *FTL OR GPLv2*. KernSmith is MIT/permissive, so we elect the
  **FTL** option and satisfy its single material obligation: the credit line in
  our documentation. We do **not** distribute under GPLv2.
- **D2 — Attribute the embolden port.** Even though algorithms are not
  copyrightable (only literal expression is), a self-described "faithful port"
  gets full FreeType attribution. This adds a FreeType credit obligation to the
  StbTrueType package; that is the honest, safe tradeoff and is preferred over
  relying on the idea/expression distinction.
- **D3 — Canonical notices file.** One `THIRD-PARTY-NOTICES.md` at the repo
  root is the single source of truth, packed into every publishable NuGet
  package and copied into app output. Exact copyright lines and full license
  texts are taken verbatim from each component's upstream `LICENSE` — never
  paraphrased or invented.
- **D4 — Cover the full distribution surface.** Document the integration/app
  chain (MonoGame Ms-PL, SDL2 Zlib, OpenAL Soft LGPL + relink note, FAudio
  Zlib, KNI Ms-PL, Gum MIT, NativeFileDialogNET Zlib) even where those
  artifacts are not yet published as binaries — future-proof and honest.
- **D5 — Document test-font licenses.** The fonts under
  `tests/KernSmith.Tests/Fixtures/` are committed to a public repo; document
  their licenses and sources even though they are not shipped in any package.

> This phase is a compliance/attribution pass, **not legal advice**. If
> KernSmith heads toward broad commercial distribution, an IP attorney should
> review the FreeType "faithful port" question (D2) specifically.

## Tasks

### Wave 1 — Research & canonical notices file
- [x] Confirm the **exact FreeType version** bundled by FreeTypeSharp 3.1.0 and
      its copyright year, for the FTL credit line.
- [x] Confirm exact copyright holders + license texts (from upstream `LICENSE`
      files) for every distributed component listed in **D4**.
- [x] **Create `THIRD-PARTY-NOTICES.md`** (repo root) covering, grouped by
      license, with name / version / project URL / SPDX / copyright / full
      license text (or required notice):
  - FreeType (native) — **FTL** — include the verbatim credit line and full FTL text; note the FTL election (D1).
  - FreeTypeSharp — MIT (© Ryan Cheung).
  - FreeType-derived embolden port (`OutlineTransforms.cs`) — covered by the FreeType credit (cross-reference D2).
  - StbImageSharp / StbImageWriteSharp / StbTrueTypeSharp — Public Domain (StbSharp / Roman Shapiro); underlying stb by Sean Barrett (Public Domain / MIT).
  - stb_truetype SDF vendored in `StbTrueTypeSdfVendored.cs` — Public Domain / MIT (Sean Barrett).
  - TerraFX.Interop.Windows — MIT (© Tanner Gooding & Contributors).
  - System.Drawing.Common (GDI backend) — MIT (© .NET Foundation & Contributors).
  - MonoGame.Framework.DesktopGL — Ms-PL (© MonoGame Foundation), and its bundled natives: SDL2 — Zlib (© Sam Lantinga); **OpenAL Soft — LGPL-2.1** (© Chris Robinson) + the relink/replace note; FAudio — Zlib (© Ethan Lee).
  - nkast.Xna.Framework / .Graphics (KNI) — Ms-PL (© Nikolas Kastellanos).
  - Gum / FlatRedBall.GumCommon / Gum.MonoGame / Gum.KNI / Gum.FNA / Gum.Themes.Editor.MonoGame — MIT (© FlatRedBall, LLC).
  - NativeFileDialogNET — Zlib, and the underlying nativefiledialog-extended — Zlib (© Bernard Teo; orig. Michael Labbe).
  - MonoGame.Extended — MIT (only if it remains referenced; currently in `Directory.Packages.props` but unreferenced).

### Wave 2 — Source-code attribution
- [x] **`src/KernSmith.Rasterizers.StbTrueType/OutlineTransforms.cs`** — add a
      file header crediting FreeType (port of `FT_Outline_EmboldenXY`), the
      FreeType copyright, and the FTL reference — matching the style of the
      existing `StbTrueTypeSdfVendored.cs` header.
- [x] **`src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeSdfVendored.cs`** —
      verify the existing Public-Domain header is accurate/sufficient (it is);
      align wording with the new header if needed.

### Wave 3 — Pack notices into NuGet packages
- [x] Add `<None Include="...\THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="/" />`
      to **every `IsPackable=true` project** so the notices travel with each
      package (core + all rasterizer backends + Gum integrations):
      `src/KernSmith`, `src/KernSmith.Rasterizers.FreeType`,
      `src/KernSmith.Rasterizers.StbTrueType`, `src/KernSmith.Rasterizers.Gdi`,
      `src/KernSmith.Rasterizers.DirectWrite.TerraFX`,
      `integrations/KernSmith.MonoGameGum`, `integrations/KernSmith.KniGum`,
      `integrations/KernSmith.GumCommon`.
- [x] Adjust relative paths per project depth (repo-root file).

### Wave 4 — README & documentation attribution
- [x] **`README.md`** (main) — add a "Third-Party Licenses / Attribution"
      section: the FreeType FTL credit line + a pointer to
      `THIRD-PARTY-NOTICES.md`; keep the existing MIT statement.
- [x] **`src/KernSmith.Rasterizers.FreeType/README.md`** — display the FreeType
      FTL credit line prominently (this package ships FreeType).
- [x] **StbTrueType package README** (if present) — note the FreeType-derived
      embolden port credit; otherwise add a short attribution line.
- [x] **GDI / DirectWrite package READMEs** — add a one-line pointer to the
      notices file where appropriate.

### Wave 5 — UI app credits / About
- [x] **`apps/KernSmith.Ui`** — expand the existing "Built with MonoGame + GUM
      UI" credit (`Layout/MainLayout.cs`) into a proper credits/About surface,
      or at minimum reference the bundled `THIRD-PARTY-NOTICES.md`. Include the
      FreeType (FTL), MonoGame (Ms-PL), SDL2 (Zlib), and **OpenAL Soft (LGPL)**
      notices, with the OpenAL relink/replace note.
- [x] Ensure `THIRD-PARTY-NOTICES.md` is copied to the UI build output
      (`<Content Include ... CopyToOutputDirectory="PreserveNewest" />`).
- [x] **CLI** (`tools/KernSmith.Cli`) — surface the notices (e.g., a
      `--licenses`/`--about` note or shipping the file alongside the binary)
      since it bundles FreeType natives.

### Wave 6 — Test-font license documentation
- [x] **Create `tests/KernSmith.Tests/Fixtures/FONT-LICENSES.md`** documenting
      each committed test font, its license, and upstream source:
      Roboto-Regular (Apache-2.0), NotoColorEmoji (OFL-1.1),
      RobotoFlex-Variable (OFL-1.1), DancingScript-Variable (OFL-1.1, under
      `tests/bmfont-compare/gum-bmfont/`).

### Wave 7 — Changelog & process note
- [x] **`CHANGELOG.md`** — add an entry (Unreleased) noting third-party
      attribution and `THIRD-PARTY-NOTICES.md` were added.
- [x] Add a short note (CLAUDE.md or `CONTRIBUTING`/RELEASING) that any new
      runtime dependency must be added to `THIRD-PARTY-NOTICES.md`.

### Wave 8 — Verification
- [x] `dotnet build` the solution to confirm csproj changes are valid.
- [x] `dotnet pack` (or inspect a packed `.nupkg`) for the FreeType + core
      packages to confirm `THIRD-PARTY-NOTICES.md` is included at the package root.
- [x] Confirm no broken README links and that the FreeType credit line text is
      verbatim.

## Acceptance criteria

- `THIRD-PARTY-NOTICES.md` exists at the repo root and accurately lists every
  distributed third-party component with verbatim copyright + license text.
- The FreeType FTL credit line appears in the notices file, the main README,
  and the FreeType package README.
- `OutlineTransforms.cs` carries a FreeType attribution header.
- Every publishable NuGet package includes `THIRD-PARTY-NOTICES.md`.
- The UI app and CLI expose / ship the notices; OpenAL LGPL relink note present.
- Test-font licenses documented.
- Solution builds and packages pack with the notices file included.

## Resolution (2026-06-03)

Delivered:
- `THIRD-PARTY-NOTICES.md` (repo root) — verbatim copyright + full license texts
  (FTL, Ms-PL, Zlib, GNU Library GPL v2), packed into all 8 publishable packages.
- `tests/KernSmith.Tests/Fixtures/FONT-LICENSES.md` — test-font licenses.
- FreeType attribution header added to `OutlineTransforms.cs`.
- `## Third-Party Licenses` sections added to the main README + all four
  rasterizer package READMEs (with the verbatim FTL credit line).
- `THIRD-PARTY-NOTICES.md` copied to UI + CLI build output; UI credit label
  updated to acknowledge FreeType.
- `CHANGELOG.md` entry; `RELEASING.md` "Adding a new package" step + checklist
  item to keep the notices file current.
- Verified: `dotnet build` (FreeType + core, net8.0/net10.0) clean; `dotnet pack`
  confirms `THIRD-PARTY-NOTICES.md` at the package root.

Deviations from the original assumptions (corrected during research):
- **FreeType version = 2.13.2**, credit-line year **2023** (confirmed against the
  FreeTypeSharp 3.1.0 pinned submodule).
- **OpenAL Soft is `LGPL-2.0-or-later`** (OpenAL Soft 1.24.3 ships the GNU
  *Library* GPL v2), not LGPL-2.1 as the plan guessed. Full text + dynamic-link
  relink note included.
- **System.Drawing.Common is NOT a dependency** — the GDI backend uses direct
  `gdi32`/`user32` P/Invoke, so no notice is required. Removed from scope.
- **Omitted** (investigated, not applicable): `FAudio` (not confirmed in the
  DesktopGL 3.8.4.1 package), `MonoGame.Extended` and `NativeFileDialogSharp`
  (defined in `Directory.Packages.props` but unreferenced by any `.csproj`).
- Exact copyright holders used verbatim from upstream (e.g. FreeTypeSharp
  `© 2022 ryancheung`; KNI `© 2014-2025 Nick Kastellanos`; Gum
  `© 2013-2024 FlatRedBall, LLC`).

Not done (deliberately out of scope; noted for the future):
- A full in-app About/credits dialog for the UI (only the credit label + shipped
  notices file were added — legally sufficient; richer UI is a future polish item).
- `Roboto-Regular.ttf` exact license build (Apache-2.0 vs OFL-1.1) could not be
  disambiguated from the embedded name table; documented both, use is permitted
  under either.
