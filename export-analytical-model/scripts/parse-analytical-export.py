from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from geometry.revit_analytical_parser import load_revit_analytical_json  # noqa: E402


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Parse an exported Revit analytical JSON payload into the shared geometry model."
    )
    parser.add_argument("input", help="Path to the analytical export JSON file.")
    parser.add_argument(
        "--normalized-output",
        help="Optional path to write the normalized parser sidecar JSON.",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    model, sidecar = load_revit_analytical_json(args.input)

    print(f"Parsed: {Path(args.input).resolve()}")
    print(f"Nodes: {len(model.nodes)}")
    print(f"Lines: {len(model.lines)}")
    print(f"Areas: {len(model.areas)}")
    print(f"Loads: {len(sidecar['loads'])}")
    if sidecar["warnings"]:
        print(f"Warnings: {len(sidecar['warnings'])}")

    if args.normalized_output:
        output_path = Path(args.normalized_output).resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(sidecar, indent=2), encoding="utf-8")
        print(f"Wrote normalized sidecar JSON to {output_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
