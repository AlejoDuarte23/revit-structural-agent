# Autodesk Automation - ExportAnalyticalModel

This folder contains the standalone APS Design Automation version of the analytical export addin for Revit 2025 and .NET 8.

It is intentionally independent from [export-analytical-model](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/export-analytical-model). The desktop addin keeps its own UI code and this automation project keeps its own duplicated export logic.

## Layout

- Project: [AnalyticalExportDA.csproj](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/src/AnalyticalExportDA/AnalyticalExportDA.csproj)
- DA app entrypoint: [App.cs](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/src/AnalyticalExportDA/App.cs)
- Export runner: [DesignAutomationExportRunner.cs](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/src/AnalyticalExportDA/DesignAutomationExportRunner.cs)
- Export logic: [AnalyticalModelExporter.cs](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/src/AnalyticalExportDA/AnalyticalModelExporter.cs)
- Bundle manifest: [PackageContents.xml](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/AnalyticalExportDA.bundle/PackageContents.xml)
- Bundle build script: [build-da-bundle.ps1](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/build-da-bundle.ps1)
- APS SDK runner: [create_activity_2025.py](/Users/alejandroduarte/.codex/worktrees/933f/revit-structural-agent/autodesk_automation%20-%20ExportAnalyticalModel/2025%20-%20Net8/create_activity_2025.py)

## Build the bundle

On Windows with Revit 2025 installed:

```powershell
powershell -ExecutionPolicy Bypass -File ".\autodesk_automation - ExportAnalyticalModel\2025 - Net8\build-da-bundle.ps1" -Configuration Release -RevitVersion 2025
```

This produces:

- `AnalyticalExportDA8.dll`
- the populated `AnalyticalExportDA.bundle/Contents`
- `files/AnalyticalExportDA.bundle.zip`

## Run with `aps-automation-sdk`

```bash
pip install aps-automation-sdk
export CLIENT_ID=your_client_id
export CLIENT_SECRET=your_client_secret
python "./autodesk_automation - ExportAnalyticalModel/2025 - Net8/create_activity_2025.py" \
  --nickname yourUniqueNickname \
  --input-rvt /absolute/path/to/model.rvt
```

The work item writes and downloads `analytical_export.json`.
