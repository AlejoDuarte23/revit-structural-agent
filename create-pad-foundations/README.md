# create-pad-foundations

This folder contains a Revit 2025 add-in that reads a JSON list of pad footing requests and places isolated square or rectangular foundations under existing structural columns in a Revit project.

The add-in targets `net8.0-windows`, which matches Revit 2025+.

## JSON contract

The input must be a JSON array.

Each footing request must include:

- `B`
- `L`
- `x`
- `y`
- `z`

All dimensions and coordinates are in meters.

Example:

```json
[
  {
    "B": 2.0,
    "L": 2.0,
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
  }
]
```

## Matching logic

For each footing request, the add-in finds the structural column whose base point matches `x`, `y`, `z` within a small 3D tolerance.

The footing is inserted at the matched column base point. Duplicate footing placements at the same point and size are skipped.

## Revit prerequisites

Before running the add-in in Revit 2025:

- Open a normal Revit project document
- Load at least one isolated structural foundation family type in the project
- The loaded footing family should expose editable type parameters for width and length, typically `Width` and `Length`

If no exact footing type exists for the requested `B` and `L`, the add-in duplicates an existing structural foundation type and resizes it.

## Trigger inside Revit

The manifest loads `PadFoundationImport.App`, which creates:

- Ribbon tab: `Structural Tools`
- Ribbon panel: `Foundations`
- Button: `Add Pad Foundations`

Clicking that ribbon button runs `CreatePadFoundationsCommand`.

## JSON path flow

When you click the button, the add-in opens a standard Windows file picker.

- You select the `.json` file
- The footing creation runs immediately
- The selected file path is remembered in `%AppData%\PadFoundationImport\settings.json`

## Build on Windows PowerShell

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pad-foundations\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025
```

Build and register the add-in manifest in one step:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pad-foundations\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RegisterAddin
```

Register only:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pad-foundations\scripts\register-addin.ps1 -Configuration Debug -RevitVersion 2025
```

## Files

- Project: [PadFoundationImport.csproj](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/PadFoundationImport.csproj)
- Revit app: [App.cs](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/App.cs)
- Import command: [CreatePadFoundationsCommand.cs](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/CreatePadFoundationsCommand.cs)
- DTOs: [PadFoundationDtos.cs](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/Models/PadFoundationDtos.cs)
- Settings store: [SettingsStore.cs](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/SettingsStore.cs)
- Manifest template: [PadFoundationImport.addin](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/src/PadFoundationImport/Manifest/PadFoundationImport.addin)
- Build script: [build-addin.ps1](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/scripts/build-addin.ps1)
- Register script: [register-addin.ps1](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/scripts/register-addin.ps1)
- Sample JSON: [pad_foundations.sample.json](/Users/alejandroduarte/.codex/worktrees/e677/revit-structural-agent/create-pad-foundations/samples/pad_foundations.sample.json)
