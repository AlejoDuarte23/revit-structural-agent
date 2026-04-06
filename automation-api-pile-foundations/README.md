# automation-api-pile-foundations

Standalone APS Design Automation scaffold for creating pile foundations in Revit from:

- an input Revit model
- a JSON payload with pile foundation requests

It follows the same repo pattern as [automation-api-footings](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-footings), but targets the existing local pile foundation workflow from [create-pile-foundations](/Users/alejandroduarte/Documents/revit-structural-agent/create-pile-foundations).

## Layout

- Bundle and APS runner: [2025 - Net8](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/2025 - Net8)
- Notebook template: [create_pile_foundations_2025.ipynb](/Users/alejandroduarte/Documents/revit-structural-agent/automation-api-pile-foundations/create_pile_foundations_2025.ipynb)

## Intended flow

1. Build the Revit 2025/.NET 8 Design Automation add-in on Windows.
2. Produce `PileFoundationsDA.bundle.zip`.
3. Use the APS SDK runner or notebook to:
   - upload `input.rvt`
   - upload `pile_foundations.json`
   - run the activity
   - download `result.rvt`

The bundle zip and sample input RVT are build/runtime artifacts and depend on a Windows/Revit build step.
