# Automation API Footings - Revit 2025 / .NET 8

This folder contains the standalone APS Design Automation version of the pad foundation workflow for Revit 2025 and .NET 8.

It is intentionally independent from [create-pad-foundations](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/create-pad-foundations). The desktop add-in keeps its own UI flow and this automation project keeps its own headless runner and duplicated placement logic.

## Inputs and output

- Input Revit model: `input.rvt`
- Input footing payload: `pad_foundations.json`
- Output Revit model: `result.rvt`

The payload matches the same contract used by [create-pad-foundations](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/create-pad-foundations):

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

## Layout

- Project: [PadFoundationsDA.csproj](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/src/PadFoundationsDA/PadFoundationsDA.csproj)
- DA app entrypoint: [App.cs](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/src/PadFoundationsDA/App.cs)
- DA runner: [DesignAutomationPadFoundationsRunner.cs](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/src/PadFoundationsDA/DesignAutomationPadFoundationsRunner.cs)
- Placement service: [FoundationPlacementService.cs](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/src/PadFoundationsDA/FoundationPlacementService.cs)
- DTOs: [PadFoundationDtos.cs](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/src/PadFoundationsDA/Models/PadFoundationDtos.cs)
- Bundle manifest: [PackageContents.xml](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/PadFoundationsDA.bundle/PackageContents.xml)
- Bundle build script: [build-da-bundle.ps1](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/build-da-bundle.ps1)
- APS SDK runner: [create_activity_2025.py](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8/create_activity_2025.py)

## Build the bundle

On Windows with Revit 2025 installed:

```powershell
powershell -ExecutionPolicy Bypass -File ".\automation-api-footings\2025 - Net8\build-da-bundle.ps1" -Configuration Release -RevitVersion 2025
```

This generates:

- `PadFoundationsDA8.dll`
- the populated `PadFoundationsDA.bundle/Contents`
- `files/PadFoundationsDA.bundle.zip`

## Run with `aps-automation-sdk`

```bash
pip install aps-automation-sdk
export CLIENT_ID=your_client_id
export CLIENT_SECRET=your_client_secret
python "./automation-api-footings/2025 - Net8/create_activity_2025.py" \
  --nickname yourUniqueNickname \
  --input-rvt /absolute/path/to/input.rvt \
  --input-json /absolute/path/to/pad_foundations.json
```

The work item writes and downloads `result.rvt`.
