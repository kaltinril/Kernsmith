# Phase 83 — Hiero UI Changes

> **Status**: Planning
> **Created**: 2026-03-22
> **Depends on**: Phase 82
> **Goal**: Update the KernSmith UI to support loading and saving `.hiero` project files alongside `.bmfc`.

---

## Current State

The UI currently has hardcoded `.bmfc` references in file dialogs, project services, drag-and-drop handling, and the init command.

Key files:
- `apps/KernSmith.Ui/ViewModels/MainViewModel.cs` — File dialog filters, save/load orchestration
- `apps/KernSmith.Ui/Services/ProjectService.cs` — Config read/write via `BmfcConfigReader`/`BmfcConfigWriter`
- `apps/KernSmith.Ui/Layout/FileBrowserDialog.cs` — File extension filtering
- `apps/KernSmith.Ui/Layout/SaveDialog.cs` — Save dialog with default extension
- `apps/KernSmith.Ui/Layout/MainLayout.cs` — Menu items for Load/Save
- `apps/KernSmith.Ui/KernSmithGame.cs` — Drag-and-drop handler
- `apps/KernSmith.Ui/Services/SessionService.cs` — Recent files tracking

## Changes Required

### 1. File Browser Dialog — Load Project

**File:** `MainViewModel.cs` (lines ~388–393)

Current:
```csharp
dialog.FileExtensionFilter = [".bmfc"];
```

Updated:
```csharp
dialog.FileExtensionFilter = [".bmfc", ".hiero"];
```

This allows users to see and select both `.bmfc` and `.hiero` files when opening a project.

### 2. Save Dialog — Format Selection

**File:** `SaveDialog.cs`

Current behavior: Takes a single `defaultExtension` parameter (hardcoded `"bmfc"`).

Options:
- **Option A (Simple)**: Detect format from current project path extension. If project was loaded as `.hiero`, save as `.hiero`. New projects default to `.bmfc`.
- **Option B (Full)**: Add a format dropdown to the save dialog allowing the user to choose between `.bmfc` and `.hiero`.

**Recommendation**: Option A for initial implementation. Option B as a follow-up if users request "Save As" format conversion.

### 3. Project Service — Format-Aware Read/Write

**File:** `ProjectService.cs`

**`SaveProject()` (lines ~15–35):**

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

**`LoadProject()` (lines ~40–115):**

Current:
```csharp
var config = BmfcConfigReader.Read(path);
```

Updated:
```csharp
var config = ConfigFormatFactory.ReadConfig(path);  // auto-detects from extension
```

### 4. Drag-and-Drop Handler

**File:** `KernSmithGame.cs` (lines ~110–121)

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

### 5. Save Project — Preserve Format

**File:** `MainViewModel.cs` (lines ~356–369)

Current:
```csharp
new SaveDialog("myproject", "bmfc")
```

Updated — detect format from current project path:
```csharp
var currentExt = CurrentProjectPath != null
    ? Path.GetExtension(CurrentProjectPath).TrimStart('.')
    : "bmfc";
new SaveDialog("myproject", currentExt)
```

This preserves the format when re-saving: if you opened a `.hiero` file, Save will write `.hiero`.

### 6. Status Bar / Title — Format Indicator (Optional)

Show which format is active in the status bar or window title:

```csharp
StatusBar.StatusText = $"Project loaded ({ext.ToUpper()}): {Path.GetFileName(path)}";
// e.g., "Project loaded (HIERO): myfont.hiero"
```

### 7. Lossy Format Warning

When saving to a format that will lose KernSmith-specific settings (e.g., saving a project with channel packing or variable font axes as `.hiero`), show a warning dialog:

```
"Some settings are not supported by the Hiero format and will not be saved:
- Channel packing
- Variable font axes
- Super sampling level
Continue saving?"
```

### 8. Session Service — Recent Projects (Optional)

**File:** `SessionService.cs`

The `LastProjectPath` already stores the full path including extension, so format detection on reload works automatically. No changes strictly required.

## Files Changed

| File | Change Type | Complexity |
|------|-------------|------------|
| `MainViewModel.cs` | Modified — dialog filters, save format detection | Low |
| `ProjectService.cs` | Modified — use `ConfigFormatFactory` | Low |
| `KernSmithGame.cs` | Modified — drag-drop extension check | Trivial |
| `SaveDialog.cs` | Possibly modified — format-aware defaults | Low |
| `MainLayout.cs` | Minor — tooltip/menu text updates | Trivial |

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
