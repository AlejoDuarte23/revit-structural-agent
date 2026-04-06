# Automation API Pile Foundations - Revit 2025 / .NET 8

This folder contains the standalone APS Design Automation version of the pile foundation workflow for Revit 2025 and .NET 8.

It is intentionally independent from [create-pile-foundations](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations). The desktop add-in keeps its own UI flow and this automation project keeps its own headless runner and duplicated placement logic.

## Inputs and output

- Input Revit model: `input.rvt`
- Input pile payload: `pile_foundations.json`
- Output Revit model: `result.rvt`

The payload matches the same contract used by [create-pile-foundations](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations):

```json
{
  "familyName": "Pile Cap-3 Round Pile",
  "typeName": "Standard",
  "units": "Millimeters",
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
  },
  "placements": [
    {
      "x": 4000,
      "y": 0,
      "z": 0
    }
  ]
}
```

The recommended shape is shared pile metadata plus a `placements` array. `B` and `L` belong to the pad-foundation workflow and should not be part of the pile payload.

Defaults applied by the automation add-in:

- `familyName`: `Pile Cap-3 Round Pile`
- `typeName`: `Standard`
- `numberOfPiles`: `3`
- `units`: `Meters`

## Layout

- Project: [PileFoundationsDA.csproj](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/src/PileFoundationsDA/PileFoundationsDA.csproj)
- DA app entrypoint: [App.cs](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/src/PileFoundationsDA/App.cs)
- DA runner: [DesignAutomationPileFoundationsRunner.cs](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/src/PileFoundationsDA/DesignAutomationPileFoundationsRunner.cs)
- Placement service: [FoundationPlacementService.cs](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/src/PileFoundationsDA/FoundationPlacementService.cs)
- DTOs: [PileFoundationDtos.cs](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/src/PileFoundationsDA/Models/PileFoundationDtos.cs)
- Bundle manifest: [PackageContents.xml](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/PileFoundationsDA.bundle/PackageContents.xml)
- Bundle build script: [build-da-bundle.ps1](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/build-da-bundle.ps1)
- APS SDK runner: [create_activity_2025.py](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8/create_activity_2025.py)

## Build the bundle

On Windows with Revit 2025 installed:

```powershell
powershell -ExecutionPolicy Bypass -File ".\automation-api-pile-foundations\2025 - Net8\build-da-bundle.ps1" -Configuration Release -RevitVersion 2025
```

This generates:

- `PileFoundationsDA8.dll`
- the populated `PileFoundationsDA.bundle/Contents`
- `files/PileFoundationsDA.bundle.zip`

## Run with `aps-automation-sdk`

```bash
pip install aps-automation-sdk
export CLIENT_ID=your_client_id
export CLIENT_SECRET=your_client_secret
python "./automation-api-pile-foundations/2025 - Net8/create_activity_2025.py" \
  --nickname yourUniqueNickname \
  --input-rvt /absolute/path/to/input.rvt \
  --input-json /absolute/path/to/pile_foundations.json
```

The work item writes and downloads `result.rvt`.
