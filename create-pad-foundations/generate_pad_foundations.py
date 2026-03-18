"""
Generate pad foundations JSON from analytical model nodes at ground level (z=0).
"""
import json
from pathlib import Path


def generate_pad_foundations(
    analytical_model_path: str,
    output_path: str,
    width_meters: float = 1.0,
    length_meters: float = 1.0,
    z_tolerance: float = 0.01
) -> None:
    """
    Read analytical model JSON and create pad foundations for all nodes at z=0.

    Args:
        analytical_model_path: Path to analytical_model.json
        output_path: Path to output pad foundations JSON
        width_meters: Width (B) of pad foundations in meters
        length_meters: Length (L) of pad foundations in meters
        z_tolerance: Tolerance for z coordinate to be considered at ground level
    """
    # Read analytical model
    with open(analytical_model_path, 'r') as f:
        analytical_model = json.load(f)

    # Filter nodes at ground level (z ≈ 0)
    ground_nodes = [
        node for node in analytical_model.get('nodes', [])
        if abs(node['z']) <= z_tolerance
    ]

    print(f"Found {len(ground_nodes)} nodes at ground level (z approx 0)")

    # Create pad foundation entries
    pad_foundations = []
    for node in ground_nodes:
        pad_foundations.append({
            "B": width_meters,
            "L": length_meters,
            "x": node['x'],
            "y": node['y'],
            "z": node['z']
        })

    # Write output JSON
    output_path_obj = Path(output_path)
    output_path_obj.parent.mkdir(parents=True, exist_ok=True)

    with open(output_path, 'w') as f:
        json.dump(pad_foundations, f, indent=2)

    print(f"Generated {len(pad_foundations)} pad foundations")
    print(f"Output written to: {output_path}")


if __name__ == "__main__":
    # Paths relative to script location
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent

    analytical_model_path = repo_root / "create-analytical-model" / "out" / "analytical_model.json"
    output_path = script_dir / "out" / "pad_foundations.json"

    # Generate pad foundations (1m x 1m)
    generate_pad_foundations(
        analytical_model_path=str(analytical_model_path),
        output_path=str(output_path),
        width_meters=1.0,
        length_meters=1.0
    )
