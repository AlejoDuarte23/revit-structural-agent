from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, Tuple

from geometry.model import Model


def _copy_metadata(payload: dict[str, Any]) -> dict[str, Any] | None:
    metadata = payload.get("metadata")
    if metadata is None:
        return None
    if not isinstance(metadata, dict):
        raise ValueError("metadata must be an object when present")
    return {"metadata": metadata}


def load_revit_analytical_json(path: str | Path) -> Tuple[Model, dict[str, Any]]:
    source_path = Path(path).resolve()
    raw_payload = json.loads(source_path.read_text(encoding="utf-8"))

    nodes = raw_payload.get("nodes", [])
    lines = raw_payload.get("lines", [])
    areas = raw_payload.get("areas", [])

    if not isinstance(nodes, list) or not isinstance(lines, list) or not isinstance(areas, list):
        raise ValueError("Expected top-level 'nodes', 'lines', and 'areas' arrays")

    model = Model()
    warnings: list[str] = []
    loads: list[dict[str, Any]] = []
    loads_by_area_id: dict[str, list[dict[str, Any]]] = {}

    for node in nodes:
        model.create_node(
            x=float(node["x"]),
            y=float(node["y"]),
            z=float(node["z"]),
            node_id=int(node["node_id"]),
            metadata=_copy_metadata(node),
        )

    for line in lines:
        model.create_line(
            ni=int(line["Ni"]),
            nj=int(line["Nj"]),
            section=str(line["section"]),
            member_type=str(line["type"]),
            line_id=int(line["line_id"]),
            metadata=_copy_metadata(line),
        )

    for area in areas:
        metadata = _copy_metadata(area)
        area_id = int(area["area_id"])
        model.create_area(
            node_ids=[int(node_id) for node_id in area["nodes"]],
            section=str(area["section"]),
            area_type=str(area["type"]),
            area_id=area_id,
            metadata=metadata,
        )

        raw_metadata = area.get("metadata")
        if not isinstance(raw_metadata, dict):
            continue

        raw_loads = raw_metadata.get("loads", [])
        if raw_loads is None:
            continue
        if not isinstance(raw_loads, list):
            warnings.append(f"Area {area_id} metadata.loads is not a list")
            continue

        normalized_area_loads: list[dict[str, Any]] = []
        for index, load in enumerate(raw_loads):
            if not isinstance(load, dict):
                warnings.append(f"Area {area_id} load #{index + 1} is not an object")
                continue

            normalized = dict(load)
            normalized["area_id"] = area_id
            normalized_area_loads.append(normalized)
            loads.append(normalized)

        if normalized_area_loads:
            loads_by_area_id[str(area_id)] = normalized_area_loads

    sidecar = {
        "source_path": str(source_path),
        "summary": {
            "nodes": len(model.nodes),
            "lines": len(model.lines),
            "areas": len(model.areas),
            "loads": len(loads),
        },
        "loads": loads,
        "loads_by_area_id": loads_by_area_id,
        "warnings": warnings,
    }
    return model, sidecar
