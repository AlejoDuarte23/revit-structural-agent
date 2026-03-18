# automation-api-footings

Standalone APS Design Automation scaffold for creating pad foundations in Revit from:

- an input Revit model
- a JSON payload with footing requests

It follows the same repo pattern as [autodesk_automation - ExportAnalyticalModel](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/autodesk_automation - ExportAnalyticalModel), but targets the existing local pad foundation workflow from [create-pad-foundations](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/create-pad-foundations).

## Layout

- Bundle and APS runner: [2025 - Net8](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/2025 - Net8)
- Notebook template: [create_pad_foundations_2025.ipynb](/Users/alejandroduarte/.codex/worktrees/6ede/revit-structural-agent/automation-api-footings/create_pad_foundations_2025.ipynb)

## Intended flow

1. Build the Revit 2025/.NET 8 Design Automation add-in on Windows.
2. Produce `PadFoundationsDA.bundle.zip`.
3. Use the APS SDK runner or notebook to:
   - upload `input.rvt`
   - upload `pad_foundations.json`
   - run the activity
   - download `result.rvt`

The bundle zip and sample input RVT are intentionally not checked in here because they depend on a Windows/Revit build step.
