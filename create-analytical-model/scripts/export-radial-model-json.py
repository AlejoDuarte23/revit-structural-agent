from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from geometry.model import RadialBuilding  # noqa: E402


def parse_floor_levels(items: list[str]) -> dict[str, float] | None:
    if not items:
        return None

    parsed: dict[str, float] = {}
    for item in items:
        if "=" not in item:
            raise ValueError(f"Invalid floor level '{item}'. Expected name=value.")

        name, raw_value = item.split("=", 1)
        parsed[name.strip()] = float(raw_value)

    return parsed


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Export radial analytical geometry to the JSON format used by the Revit add-in."
    )
    parser.add_argument("--output", required=True, help="Output JSON path.")
    parser.add_argument("--diameter", type=float, default=32.0, help="Building diameter in meters.")
    parser.add_argument("--core-diameter", type=float, default=8.0, help="Core diameter in meters.")
    parser.add_argument("--slices", type=int, default=20, help="Number of radial slices.")
    parser.add_argument("--inner-ring-count", type=int, default=2, help="Number of inner rings.")
    parser.add_argument(
        "--floor-level",
        action="append",
        default=[],
        help="Floor level in the form name=value where value is meters. Repeat for multiple floors.",
    )
    parser.add_argument(
        "--mode",
        choices=("building", "floor"),
        default="building",
        help="Export the full multistory model or a single floor plan.",
    )
    parser.add_argument(
        "--floor-z",
        type=float,
        default=4.0,
        help="Z elevation in meters for --mode floor.",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    building = RadialBuilding(
        diameter=args.diameter,
        core_diameter=args.core_diameter,
        n_slices=args.slices,
        inner_ring_count=args.inner_ring_count,
        floor_levels=parse_floor_levels(args.floor_level),
    )

    model = building.generate() if args.mode == "building" else building.generate_floor_plan(z=args.floor_z)

    payload = {
        "nodes": model.export_nodes(),
        "lines": model.export_lines(),
        "areas": model.export_areas(),
    }

    output_path = Path(args.output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    print(f"Wrote analytical model JSON to {output_path}")
    print(f"Nodes: {len(payload['nodes'])}")
    print(f"Lines: {len(payload['lines'])}")
    print(f"Areas: {len(payload['areas'])}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
