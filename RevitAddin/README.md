# RevitAddin

This folder contains a Revit 2025 add-in that imports the analytical geometry produced by the Python model in this repository.

The add-in targets `net8.0-windows`, which matches Revit 2025+. I did not compile it in this environment.

## Geometry contract

The JSON shape matches the existing Python exports in [geometry/model.py](/Users/alejandroduarte/Documents/revit-structural-agent/geometry/model.py):

- `Model.export_nodes()` writes `node_id`, `x`, `y`, `z`
- `Model.export_lines()` writes `line_id`, `Ni`, `Nj`, `section`, `type`
- `Model.export_areas()` writes `area_id`, `nodes`, `section`, `type`

The current radial generator also defines the structural naming that Revit will see:

- Member sections come from `SECTIONS`
- Slab sections come from `AREA_SECTIONS`
- Line type `"column"` becomes an analytical column
- Any other line type becomes an analytical beam
- Area type `"slab"` becomes an analytical floor panel

Coordinates are assumed to be meters. Slab thickness is parsed from section strings such as `SLAB150` meaning `150 mm`.

## Trigger inside Revit

The manifest loads `AnalyticalImport.App`, which creates:

- Ribbon tab: `Analytical Tools`
- Ribbon panel: `Import`
- Button: `Import Analytical JSON`

Clicking that ribbon button runs `ImportAnalyticalModelCommand`.

## How the JSON path is entered

When you click the button, the add-in opens a standard Windows file picker.

- You select the `.json` file
- The import runs immediately
- The selected file path is remembered in `%AppData%\AnalyticalImport\settings.json`

So there is no hard-coded path and no manual typing by default.

## Revit prerequisites

Before running the add-in in Revit 2025:

- Load Structural Framing types with the same names used by the beam/bracing sections, for example `UB203X133X25`
- Load Structural Columns types with the same names used by the column sections, for example `UC254X254X73`
- Use a project that supports `AnalyticalMember` and `AnalyticalPanel`
- `Steel` and `Concrete` materials can be missing; the add-in creates them by name if needed

If your Revit type names do not exactly match the JSON section names, edit `SectionNameMap` in [ImportAnalyticalModelCommand.cs](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/ImportAnalyticalModelCommand.cs).

## Build on Windows PowerShell

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\RevitAddin\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025
```

If Revit is installed in a non-default folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\RevitAddin\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RevitInstallDir "C:\Program Files\Autodesk\Revit 2025"
```

Build and register the add-in manifest in one step:

```powershell
powershell -ExecutionPolicy Bypass -File .\RevitAddin\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RegisterAddin
```

Register only:

```powershell
powershell -ExecutionPolicy Bypass -File .\RevitAddin\scripts\register-addin.ps1 -Configuration Debug -RevitVersion 2025
```

The manifest is copied to:

- Current user: `%AppData%\Autodesk\Revit\Addins\2025\AnalyticalImport.addin`
- All users: `%ProgramData%\Autodesk\Revit\Addins\2025\AnalyticalImport.addin`

## Export JSON from the current Python geometry

A helper script is included to export the radial geometry straight from this repo:

```powershell
python .\RevitAddin\scripts\export-radial-model-json.py --output .\RevitAddin\out\analytical_model.json
```

Example with custom floors:

```powershell
python .\RevitAddin\scripts\export-radial-model-json.py --output .\RevitAddin\out\analytical_model.json --floor-level floor_1=4 --floor-level floor_2=8 --floor-level floor_3=12
```

## Files

- Project: [AnalyticalImport.csproj](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/AnalyticalImport.csproj)
- Revit app: [App.cs](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/App.cs)
- Import command: [ImportAnalyticalModelCommand.cs](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/ImportAnalyticalModelCommand.cs)
- DTOs: [ModelDtos.cs](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/Models/ModelDtos.cs)
- Settings store: [SettingsStore.cs](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/SettingsStore.cs)
- Manifest template: [AnalyticalImport.addin](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/src/AnalyticalImport/Manifest/AnalyticalImport.addin)
- Build script: [build-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/scripts/build-addin.ps1)
- Register script: [register-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/RevitAddin/scripts/register-addin.ps1)
