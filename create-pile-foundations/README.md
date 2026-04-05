# create-pile-foundations

This folder contains a Revit 2025 add-in that reads a JSON list of pile foundation requests and places loaded structural foundation family instances under existing structural columns in a Revit project.

It follows the same repo pattern as [create-pad-foundations](/Users/alejandroduarte/Documents/revit-structural-agent/create-pad-foundations), but targets pile-cap style structural foundation families that already exist in the Revit model.

The add-in targets `net8.0-windows`, which matches Revit 2025+.

## JSON contract

The input must be a JSON array.

Each request must include:

- `x`
- `y`
- `z`

Optional request properties:

- `units` with one of `Millimeters`, `Centimeters`, `Meters`, `Inches`, or `Feet`
- `targetTypeName`
- `parameters`

Defaults used when omitted:

- `familyName`: `Pile Cap-3 Round Pile`
- `typeName`: `Standard`
- `numberOfPiles`: always `3`
- `units`: `Meters`

`typeName` is the loaded template type already present in the Revit project. If `parameters` are provided, the add-in duplicates that loaded type into a repo-generated type name unless `targetTypeName` is explicitly supplied.

Unknown properties are ignored. That means you can reuse pad-style coordinate payloads that still include fields like `B` and `L`; this add-in will only read the location fields plus its own optional pile settings.

Example:

```json
[
  {
    "B": 1.0,
    "L": 1.0,
    "x": 4.0,
    "y": 0.0,
    "z": 0.0,
    "parameters": {
      "foundationThickness": 750,
      "widthIndent": 500,
      "pileLength": 6000,
      "pileDiameter": 450,
      "pileCentresVertical": 1350,
      "pileCentresHorizontal": 1350,
      "length1": 450,
      "length2": 750,
      "pileCutOut": 75,
      "clearance": 375
    }
  }
]
```

## Matching logic

For each request, the add-in finds the structural column whose base point matches `x`, `y`, `z` within the same small 3D tolerance used by the pad foundation workflow.

The pile foundation family instance is inserted at the matched column base point. Existing structural foundations already placed at the same location are treated as duplicates and skipped.

## Revit prerequisites

Before running the add-in in Revit 2025:

- Open a normal Revit project document
- Load the structural foundation family and template type that you reference in JSON
- Ensure the family exposes the editable type parameters you want to drive, such as `Number of Piles`, `Pile Length`, `Pile Diameter`, and related pile-cap dimensions

If you rely on defaults, the model must already contain family `Pile Cap-3 Round Pile` with type `Standard`.

Unlike the external reference importer, this repo version does not load `.rfa` files from disk. The requested family type must already exist in the Revit model.

## Trigger inside Revit

The manifest loads `PileFoundationImport.App`, which creates:

- Ribbon tab: `Structural Tools`
- Ribbon panel: `Foundations`
- Button: `Add Pile Foundations`

Clicking that ribbon button runs `CreatePileFoundationsCommand`.

## JSON path flow

When you click the button, the add-in opens a standard Windows file picker.

- You select the `.json` file
- The pile foundation creation runs immediately
- The selected file path is remembered in `%AppData%\PileFoundationImport\settings.json`

## Build on Windows PowerShell

From the repository root on Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pile-foundations\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025
```

Build and register the add-in manifest in one step:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pile-foundations\scripts\build-addin.ps1 -Configuration Debug -RevitVersion 2025 -RegisterAddin
```

Register only:

```powershell
powershell -ExecutionPolicy Bypass -File .\create-pile-foundations\scripts\register-addin.ps1 -Configuration Debug -RevitVersion 2025
```

## Files

- Project: [PileFoundationImport.csproj](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/PileFoundationImport.csproj)
- Revit app: [App.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/App.cs)
- Import command: [CreatePileFoundationsCommand.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/CreatePileFoundationsCommand.cs)
- DTOs: [PileFoundationDtos.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/Models/PileFoundationDtos.cs)
- Settings store: [SettingsStore.cs](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/SettingsStore.cs)
- Manifest template: [PileFoundationImport.addin](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/src/PileFoundationImport/Manifest/PileFoundationImport.addin)
- Build script: [build-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/scripts/build-addin.ps1)
- Register script: [register-addin.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/scripts/register-addin.ps1)
- Sample JSON: [pile_foundations.sample.json](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations/samples/pile_foundations.sample.json)
