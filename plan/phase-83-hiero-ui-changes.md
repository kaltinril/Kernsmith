# Phase 83 — Hiero UI Changes

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phase 82
> **Goal**: Update the KernSmith UI to support loading and saving `.hiero` project files alongside `.bmfc`.

---

## Current State

The UI currently has hardcoded `.bmfc` references in file dialogs, project services, drag-and-drop handling, and the init command.

Key files:
- `apps/KernSmith.Ui/ViewModels/MainViewModel.cs` — File dialog filters and save/load orchestration. File dialogs use `NativeFileDialog` (NativeFileDialogNET) inline: the load-project dialog is around lines 443–451 and the save-project dialog around lines 406–422, both using the fluent `.AddFilter(label, extensions)` API where `extensions` is a comma-separated list of bare extensions (no dots), e.g. `"bmfc,hiero"`.
- `apps/KernSmith.Ui/Services/ProjectService.cs` — Config read/write. `SaveProject()` (~lines 16–35) uses `BmfcConfigWriter.WriteToFile()`; `LoadProject()` (~lines 40–112) uses `BmfcConfigReader.Read()`; `CurrentProjectPath` property (~line 11).
- `apps/KernSmith.Ui/Layout/MainLayout.cs` — Menu items / tooltip text for Load/Save (text-only changes; needs a detail pass).
- `apps/KernSmith.Ui/KernSmithGame.cs` — Drag-and-drop handler (~lines 93–104); the extension check `if (ext is ".bmfc")` is at ~line 99.
- `apps/KernSmith.Ui/Services/SessionService.cs` — Recent files tracking. `LastProjectPath` is stored as a full path including extension, so format is auto-detected on reload — no change needed.

> **There are NO `FileBrowserDialog.cs` or `SaveDialog.cs` files** — those do not exist in the codebase. All dialog work happens inline in `MainViewModel.cs` via `NativeFileDialog`.

> **Blocking dependency:** Phase 83 requires Phase 82's `ConfigFormatFactory`, `HieroConfigReader`, and `HieroConfigWriter` to be merged as public APIs before this phase can begin.

## Changes Required

### 1. Load Project Dialog — Filter

**File:** `MainViewModel.cs` (load-project dialog, ~lines 443–451)

The load dialog uses `NativeFileDialog`'s fluent `.AddFilter(label, extensions)` API. Update the config filter so it accepts both formats (extensions are comma-separated, no dots):

```csharp
.AddFilter("Config files", "bmfc,hiero")
```

This allows users to see and select both `.bmfc` and `.hiero` files when opening a project.

### 2. Save Project Dialog — Filter + Default Filename

**File:** `MainViewModel.cs` (save-project dialog, ~lines 406–422)

Update the save dialog's `.AddFilter(...)` to include `"bmfc,hiero"`, and make the default filename **format-aware**: derive the default extension from `_projectService.CurrentProjectPath` when set (so re-saving a `.hiero` project defaults to `.hiero`), otherwise default to `.bmfc`. See the re-save edge cases below for the fallback rules.

A format dropdown / explicit "Save As format" picker is a possible follow-up but is not required for this phase.

### 3. Project Service — Format-Aware Read/Write

**File:** `ProjectService.cs`

**`SaveProject()` (~lines 16–35):**

Current:
```csharp
var config = BmfcConfig.FromOptions(options);
BmfcConfigWriter.WriteToFile(config, path);
```

Updated:
```csharp
var config = BmfcConfig.FromOptions(options);
ConfigFormatFactory.WriteConfig(config, path);  // auto-detects from extension
```

**`LoadProject()` (~lines 40–112):**

Current:
```csharp
var config = BmfcConfigReader.Read(path);
```

Updated:
```csharp
var config = ConfigFormatFactory.ReadConfig(path);  // auto-detects from extension
```

### 4. Drag-and-Drop Handler

**File:** `KernSmithGame.cs` (drag-drop handler ~lines 93–104; the extension check is at ~line 99)

Current:
```csharp
if (ext is ".bmfc")
    _mainViewModel!.LoadProjectFromPath(path);
```

Updated:
```csharp
if (ext is ".bmfc" or ".hiero")
    _mainViewModel!.LoadProjectFromPath(path);
```

### 5. Save Project — Preserve Format + Re-save Edge Cases

**File:** `MainViewModel.cs` (save-project dialog, ~lines 406–422)

Derive the default filename's extension from `_projectService.CurrentProjectPath`:

```csharp
var currentExt = _projectService.CurrentProjectPath != null
    ? Path.GetExtension(_projectService.CurrentProjectPath).TrimStart('.')
    : "bmfc";
```

> **Note:** `CurrentProjectPath` lives on `ProjectService` (~line 11), not on `MainViewModel` directly.

This preserves the format when re-saving: if you opened a `.hiero` file, Save defaults to `.hiero`.

**Re-save format edge cases:**
- If `CurrentProjectPath` is **null**, OR its extension is **neither `.bmfc` nor `.hiero`**, default to `.bmfc`.
- The save dialog's default filename should match the detected extension (`.bmfc` or `.hiero`).
- Final write goes through `ConfigFormatFactory.WriteConfig()` (in `ProjectService.SaveProject()`), which auto-detects format from the chosen path's extension.

### 6. Status Bar / Title — Format Indicator (Optional)

Show which format is active in the status bar or window title:

```csharp
StatusBar.StatusText = $"Project loaded ({ext.ToUpper()}): {Path.GetFileName(path)}";
// e.g., "Project loaded (HIERO): myfont.hiero"
```

### 7. Lossy Format Warning

When saving to a format that will lose KernSmith-specific settings (e.g., saving a project with channel packing or variable font axes as `.hiero`), show a warning before writing:

```
"Some settings are not supported by the Hiero format and will not be saved:
- Channel packing
- Variable font axes
- Super sampling level
Continue saving?"
```

**Implementation:** Reuse the existing `ErrorDialog` with a confirm / "Save Anyway" button. The lossy-settings check runs **in the `MainViewModel` save flow, before calling `ProjectService.SaveProject()`**. The dialog must allow the user to **cancel** (abort the save) as well as "Save Anyway" (proceed). Only run this check when the target format actually drops settings (e.g., saving as `.hiero` with unmapped KernSmith features present).

### 8. Session Service — Recent Projects (Optional)

**File:** `SessionService.cs`

The `LastProjectPath` already stores the full path including extension, so format detection on reload works automatically. No changes strictly required.

## Files Changed

| File | Change Type | Complexity |
|------|-------------|------------|
| `MainViewModel.cs` | Modified — load/save dialog filters → `"bmfc,hiero"`, format-aware default filename on save, lossy-format warning in save flow | Low |
| `ProjectService.cs` | Modified — use `ConfigFormatFactory` instead of `BmfcConfigReader`/`BmfcConfigWriter` directly | Low |
| `KernSmithGame.cs` | Modified — drag-drop accepts `.hiero` too (extension check ~line 99) | Trivial |
| `MainLayout.cs` | Modified — menu/tooltip text only (needs a detail pass) | Trivial |

## Test Plan

| Test | Description |
|------|-------------|
| Load .hiero via file dialog | File browser shows .hiero files, loads correctly |
| Load .hiero via drag-drop | Dropping .hiero file opens project |
| Save .hiero preserves format | Open .hiero → Save → writes .hiero (not .bmfc) |
| Save new project defaults to .bmfc | New project without prior path saves as .bmfc |
| Lossy format warning | Saving with unsupported features shows warning |
| Recent file reloads correct format | Session restore loads .hiero project correctly |

## Estimated Complexity

- **Modified code**: ~20–30 lines across 3–4 files
- **Risk**: Very low — mostly string/extension changes
- **Dependencies**: Phase 82 (`ConfigFormatFactory` must exist)

---

> **Plan review 2026-03-24**: Updated line number references for `MainViewModel.cs` (FileExtensionFilter at ~401-404, SaveDialog at ~377-379). Corrected `CurrentProjectPath` reference — it lives on `ProjectService`, not `MainViewModel`. Updated `ProjectService` line references.
