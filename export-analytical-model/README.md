# export-analytical-model

This folder contains a Revit 2025 add-in that exports the current structural analytical model to the geometry JSON used in this repository.

The add-in targets `net8.0-windows`, which matches Revit 2025+.

## Export contract

The output JSON keeps the geometry-first shape:

- `nodes`
- `lines`
- `areas`

Each record can also carry optional `metadata`.

- `nodes[]` export `node_id`, `x`, `y`, `z`
- `lines[]` export `line_id`, `Ni`, `Nj`, `section`, `type`
- `areas[]` export `area_id`, `nodes`, `section`, `type`

Revit-specific values are stored under `metadata`, including:

- element ids and unique ids
- structural role
- material name
- section shape or thickness
- physical counterpart ids when available
- hosted slab load metadata for exported analytical panels

Coordinates are exported in meters. Panel thickness is exported in millimeters. Hosted area-load vectors are exported in global `kN/m^2`.

## Current scope

The exporter currently supports:

- `AnalyticalMember` elements with straight `Line` geometry
- `AnalyticalPanel` elements with linear outer contours
- analytical openings serialized under panel metadata
- hosted and constrained `AreaLoad` loads with one reference point

The exporter intentionally skips:

- curved analytical members
- curved panel edges
- line loads
- point loads
- non-uniform area loads

Skipped elements are reported in the Revit completion dialog.

## Trigger inside Revit

The manifest loads `AnalyticalExport.App`, which creates:

- Ribbon tab: `Analytical Tools`
- Ribbon panel: `Export`
- Button: `Export Analytical JSON`

Clicking that ribbon button runs `ExportAnalyticalModelCommand`.

## How the JSON path is entered

When you click the button, the add-in opens a standard Windows save-file dialog.

- You select the destination `.json` path
- The export runs immediately
- The selected path is remembered in `%AppData%\AnalyticalExport\settings.json`

## Build on Windows PowerShell

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\export-analytical-model\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025
```

Build and register the add-in manifest in one step:

```powershell
powershell -ExecutionPolicy Bypass -File .\export-analytical-model\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RegisterAddin
```

Register only:

```powershell
powershell -ExecutionPolicy Bypass -File .\export-analytical-model\scripts\register-addin.ps1 -Configuration Debug -RevitVersion 2025
```

## Parse exported JSON in Python

Parse the export into the shared Python geometry model:

```powershell
python .\export-analytical-model\scripts\parse-analytical-export.py .\export-analytical-model\samples\analytical_export.sample.json
```

Write a normalized sidecar summary:

```powershell
python .\export-analytical-model\scripts\parse-analytical-export.py .\export-analytical-model\samples\analytical_export.sample.json --normalized-output .\export-analytical-model\out\normalized.json
```

## Files

- Project: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/AnalyticalExport.csproj`
- Revit app: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/App.cs`
- Export command: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/ExportAnalyticalModelCommand.cs`
- DTOs: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/Models/ModelDtos.cs`
- Settings store: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/SettingsStore.cs`
- Manifest template: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/src/AnalyticalExport/Manifest/AnalyticalExport.addin`
- Build script: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/scripts/build-addin.ps1`
- Register script: `/Users/alejandroduarte/Documents/revit-structural-agent/export-analytical-model/scripts/register-addin.ps1`
- Parser: `/Users/alejandroduarte/Documents/revit-structural-agent/geometry/revit_analytical_parser.py`
