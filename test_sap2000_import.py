import json
from pathlib import Path
from sap2000.revit_analytical_import import RevitAnalyticalSapGenerator
 
source = Path("sample_analytical_export.json")
patched = Path("out/sample_analytical_export.with_supports.json")
data = json.loads(source.read_text())
 
min_z = min(node["z"] for node in data["nodes"])
for node in data["nodes"]:
    if abs(node["z"] - min_z) < 1e-9:
        node.setdefault("metadata", {})["support"] = {"restraint": [1, 1, 1, 1, 1, 1]}
 
patched.parent.mkdir(parents=True, exist_ok=True)
patched.write_text(json.dumps(data, indent=2))
 
result = RevitAnalyticalSapGenerator(
    patched,
    analysis_model_path="out/sample_analytical_export.sdb",
).generate()
 
print(result["analysis"])
print(len(result["support_reactions"]["supports"]) if result["support_reactions"] else 0)