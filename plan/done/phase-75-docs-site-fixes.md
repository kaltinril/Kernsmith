# Phase 75 — DocFX Documentation Site Fixes

> **Status**: Completed
> **Created**: 2026-03-22
> **Completed**: 2026-03-22
> **Goal**: Fix issues found on the deployed DocFX site at https://kaltinril.github.io/Kernsmith/.

---

## Issues

| # | Issue | Category |
|---|-------|----------|
| 1 | Giant "D" default logo in top-left nav — no custom logo configured | Branding |
| 2 | Landing page (`index.md`) has no links to documentation sections | Navigation |
| 3 | Core docs class/namespace references are plain text, not xref links | Linking |
| 4 | CLI and UI docs may have similar unlinked class references | Linking |
| 5 | "API Reference" row in `docs/index.md` says "see top navigation" instead of linking | Linking |

---

## Details

### Issue 1 — Giant "D" Default Logo

The site shows DocFX's default logo (a big "D") in the top-left navigation because no custom logo is configured in `docfx.json`. The project has custom icons at `assets/icon.png` and `assets/icon_alt.png` but they aren't being used.

**Fix**: Add `_appLogoPath`, `_appFaviconPath`, and `_appLogoUrl` to `globalMetadata` in `docfx.json` pointing to the project's own icon assets. May need to create an SVG version or use the PNG. Set `_appLogoUrl` to point to the home page.

### Issue 2 — Landing Page Has No Links

The root `index.md` uses `_layout: landing` but just says "Explore the documentation sections below" with NO actual links. The `docs/index.md` has proper links (Core Library, CLI Reference, UI Guide table) but the landing page is completely bare.

**Fix**: Add documentation section cards/links to `index.md` so users can navigate from the home page. Could mirror the table from `docs/index.md` or add landing-page-style section cards.

### Issue 3 — Core Docs: Most Class/Namespace References Are Not Linked

In `docs/core/index.md`, only one `<xref:KernSmith.BmFont>` cross-reference link exists (on the BmFont entry in the Namespaces table). All other class references (`BmFontResult`, `FontGeneratorOptions`, `CharacterSet`, `FontInfo`, `KerningPair`, `GlyphMetrics`, `HeadTable`, `HheaTable`, `Os2Metrics`, `NameInfo`, `IRasterizer`, `FreeTypeRasterizer`, `IGlyphEffect`, `GlyphCompositor`, `IAtlasPacker`, `AtlasBuilder`, `FileWriter`, `BmFontResult`, `BmFontReader`, `BmFontModel`, `InfoBlock`, `CommonBlock`) are plain text or inline code, not xref links.

**Fix**: Convert inline code class references to DocFX `<xref:FullyQualifiedName>` cross-reference links so they link to the auto-generated API pages. The API pages do exist (confirmed `api/KernSmith.BmFont.html` loads correctly with full content).

### Issue 4 — CLI and UI Docs May Have Similar Linking Issues

The CLI (`docs/cli/index.md`, `docs/cli/commands.md`) and UI (`docs/ui/index.md`) docs should also be reviewed for unlinked class references that could use xref links.

### Issue 5 — API Reference Row Not Linked

In the `docs/index.md` documentation sections table, the "API Reference" row says "see top navigation" instead of having a direct link. Should link to `../api/KernSmith.html` or similar.

---

## Tasks

### Task 1 — Configure custom logo
- [ ] Add `_appLogoPath` to `globalMetadata` in `docfx.json` pointing to project icon
- [ ] Add `_appFaviconPath` to `globalMetadata` in `docfx.json`
- [ ] Add `_appLogoUrl` to `globalMetadata` pointing to the home page
- [ ] Verify icon assets are included in the DocFX build output

### Task 2 — Fix landing page
- [ ] Add documentation section links/cards to root `index.md`
- [ ] Include links to Core Library, CLI Reference, UI Guide, and API Reference
- [ ] Ensure links work from the deployed site root

### Task 3 — Add xref links to core docs
- [ ] Convert all plain-text class/interface references in `docs/core/index.md` to `<xref:Namespace.TypeName>` links
- [ ] Verify each xref resolves to an existing API page

### Task 4 — Add xref links to CLI docs
- [ ] Review `docs/cli/index.md` for unlinked class references
- [ ] Review `docs/cli/commands.md` for unlinked class references
- [ ] Convert to `<xref:Namespace.TypeName>` links where appropriate

### Task 5 — Add xref links to UI docs
- [ ] Review `docs/ui/index.md` for unlinked class references
- [ ] Convert to `<xref:Namespace.TypeName>` links where appropriate

### Task 6 — Fix API Reference link in docs index
- [ ] Change "see top navigation" text in `docs/index.md` API Reference row to a direct link

### Task 7 — Test locally
- [ ] Run `dotnet docfx docfx.json --serve` to build and preview the site
- [ ] Verify custom logo appears in top-left navigation
- [ ] Verify favicon appears in browser tab
- [ ] Verify landing page links navigate correctly
- [ ] Verify xref links in core, CLI, and UI docs resolve to API pages
- [ ] Verify API Reference link in docs index works

---

## Key Files

| File | Purpose |
|------|---------|
| `docfx.json` | DocFX configuration — logo, metadata, templates |
| `index.md` | Site landing page |
| `docs/index.md` | Documentation hub with section table |
| `docs/core/index.md` | Core library documentation |
| `docs/cli/index.md` | CLI overview |
| `docs/cli/commands.md` | CLI command reference |
| `docs/ui/index.md` | UI guide |
| `assets/icon.png` | Project icon (logo candidate) |
| `assets/icon_alt.png` | Alternate project icon |
