# create-revit-model

This folder contains a Revit 2025 add-in that imports the same JSON contract used by `create-analytical-model`, but creates physical structural elements in a normal Revit project:

- Structural framing family instances for beams
- Structural column family instances for columns
- Structural floor elements for slabs

The add-in targets `net8.0-windows`, which matches Revit 2025+. I did not compile it in this environment.

## JSON contract

The JSON shape is intentionally the same as [ModelDtos.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-analytical-model/src/AnalyticalImport/Models/ModelDtos.cs):

- `nodes`: `node_id`, `x`, `y`, `z`
- `lines`: `line_id`, `Ni`, `Nj`, `section`, `type`
- `areas`: `area_id`, `nodes`, `section`, `type`

Coordinates are assumed to be meters, matching the Python geometry model in [geometry/model.py](/Users/alejandroduarte/Documents/revit-structural-agent/geometry/model.py).

Mapping rules:

- `line.type == "column"` creates a structural column
- Any other line creates structural framing
- `areas` create floor slabs
- Floor types are resolved by `area.section`; if a floor type does not exist, the add-in duplicates Revit's default floor type and sets the thickness parsed from the section string, for example `Concrete 150mm`

## Trigger inside Revit

The manifest loads `RevitModelImport.App`, which creates:

- Ribbon tab: `Structural Tools`
- Ribbon panel: `Import`
- Button: `Create Revit Model`

Clicking that ribbon button runs `CreateRevitModelCommand`.

## JSON path flow

When you click the button, the add-in opens a standard Windows file picker.

- You select the `.json` file
- The model creation runs immediately
- The selected file path is remembered in `%AppData%\RevitModelImport\settings.json`

So there is no hard-coded path and no manual typing by default.

## Revit prerequisites

Before running the add-in in Revit 2025:

- Load structural framing types with the beam names referenced by the JSON, for example `UB254x146x31`
- Load structural column types with the column names referenced by the JSON, for example `UC356x406x551`
- Open a normal Revit project, not a family document
- A default floor type must exist in the project so the add-in can duplicate it when a slab type name from JSON is missing

Like the analytical importer, the command includes the same section aliases in `SectionNameMap` for the steel member types.

## Build on Windows PowerShell

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-revit-model\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025
```

If Revit is installed in a non-default folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-revit-model\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RevitInstallDir "C:\Program Files\Autodesk\Revit 2025"
```

Build and register the add-in manifest in one step:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-revit-model\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RegisterAddin
```

Register only:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-revit-model\scripts\register-addin.ps1 -Configuration Debug -RevitVersion 2025
```

## Files

- Project: [RevitModelImport.csproj](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/RevitModelImport.csproj)
- Revit app: [App.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/App.cs)
- Import command: [CreateRevitModelCommand.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/CreateRevitModelCommand.cs)
- DTOs: [ModelDtos.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/Models/ModelDtos.cs)
- Settings store: [SettingsStore.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/SettingsStore.cs)
- Manifest template: [RevitModelImport.addin](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/src/RevitModelImport/Manifest/RevitModelImport.addin)
- Build script: [build-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/scripts/build-addin.ps1)
- Register script: [register-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/create-revit-model/scripts/register-addin.ps1)
