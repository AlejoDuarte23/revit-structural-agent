import math
from typing import Dict, List, Optional, Tuple


class Model:
    def __init__(self) -> None:
        self.current_node_id = 0
        self.current_line_id = 0
        self.nodes: Dict[int, dict] = {}
        self.lines: Dict[int, dict] = {}

    def create_node_id(self) -> int:
        self.current_node_id += 1
        return self.current_node_id

    def create_line_id(self) -> int:
        self.current_line_id += 1
        return self.current_line_id

    def create_node(
        self,
        x: float,
        y: float,
        z: float = 0.0,
        node_id: Optional[int] = None,
    ) -> int:
        nid = self.create_node_id() if node_id is None else int(node_id)
        if nid in self.nodes:
            raise ValueError(f"Node {nid} already exists")

        self.nodes[nid] = {
            "node_id": nid,
            "x": float(x),
            "y": float(y),
            "z": float(z),
        }
        self.current_node_id = max(self.current_node_id, nid)
        return nid

    def create_line(
        self,
        ni: int,
        nj: int,
        section: str,
        member_type: str,
        line_id: Optional[int] = None,
    ) -> int:
        if ni not in self.nodes or nj not in self.nodes:
            raise ValueError("Both Ni and Nj must already exist")

        lid = self.create_line_id() if line_id is None else int(line_id)
        if lid in self.lines:
            raise ValueError(f"Line {lid} already exists")

        self.lines[lid] = {
            "line_id": lid,
            "Ni": int(ni),
            "Nj": int(nj),
            "section": str(section),
            "type": str(member_type),
        }
        self.current_line_id = max(self.current_line_id, lid)
        return lid

    def export_nodes(self) -> List[dict]:
        return [self.nodes[nid] for nid in sorted(self.nodes)]

    def export_lines(self) -> List[dict]:
        return [self.lines[lid] for lid in sorted(self.lines)]


SECTIONS = {
    "core ring": "UB 203x133x25",
    "inner ring": "UB 254x146x31",
    "middle ring": "UB 305x165x40",
    "outer ring": "UB 356x171x45",
    "nerve": "UB 203x102x23",
    "column": "UC 254x254x73",
}


class RadialBuilding:
    def __init__(
        self,
        diameter: float = 32.0,
        core_diameter: float = 8.0,
        n_slices: int = 20,
        inner_ring_count: int = 2,
        ring_radii: Optional[List[float]] = None,
        floor_levels: Optional[Dict[str, float]] = None,
    ) -> None:
        self.diameter = diameter
        self.core_diameter = core_diameter
        self.n_slices = n_slices
        self.inner_ring_count = inner_ring_count
        self.ring_radii = ring_radii
        self.floor_levels = floor_levels or {
            "floor_1": 5.0,
            "floor_2": 8.0,
            "floor_3": 12.0,
        }

    @staticmethod
    def polar_xy(radius: float, angle_rad: float) -> Tuple[float, float]:
        return radius * math.cos(angle_rad), radius * math.sin(angle_rad)

    def _resolved_ring_radii(self) -> List[float]:
        if self.ring_radii is not None:
            return self.ring_radii

        core_radius = self.core_diameter / 2.0
        outer_radius = self.diameter / 2.0
        total_ring_count = self.inner_ring_count + 2
        if total_ring_count < 2:
            raise ValueError("inner_ring_count must be >= 0")

        if total_ring_count == 2:
            return [core_radius, outer_radius]

        step = (outer_radius - core_radius) / (total_ring_count - 1)
        return [core_radius + step * index for index in range(total_ring_count)]

    def _validate_geometry(self) -> List[Tuple[str, float]]:
        outer_radius = self.diameter / 2.0
        core_radius = self.core_diameter / 2.0
        ring_radii = self._resolved_ring_radii()

        if self.n_slices < 3:
            raise ValueError("n_slices must be >= 3")
        if self.inner_ring_count < 0:
            raise ValueError("inner_ring_count must be >= 0")
        if len(ring_radii) < 2:
            raise ValueError("ring_radii must contain at least 2 values")
        if abs(ring_radii[0] - core_radius) > 1e-9:
            raise ValueError("First ring radius must match core_diameter / 2")
        if abs(ring_radii[-1] - outer_radius) > 1e-9:
            raise ValueError("Last ring radius must match diameter / 2")
        if sorted(ring_radii) != ring_radii:
            raise ValueError("ring_radii must be sorted in ascending order")
        if not self.floor_levels:
            raise ValueError("floor_levels must not be empty")

        ordered_floors = sorted(self.floor_levels.items(), key=lambda item: item[1])
        z_levels = [z for _, z in ordered_floors]
        if any(z <= 0.0 for z in z_levels):
            raise ValueError("All floor z values must be greater than 0.0")
        if len(set(z_levels)) != len(z_levels):
            raise ValueError("Floor z values must be unique")

        return ordered_floors

    def _ring_member_types(self) -> List[str]:
        ring_radii = self._resolved_ring_radii()
        ring_types: List[str] = []
        for i, _ in enumerate(ring_radii):
            if i == 0:
                ring_types.append("core ring")
            elif i == len(ring_radii) - 1:
                ring_types.append("outer ring")
            elif len(ring_radii) == 3:
                ring_types.append("inner ring")
            elif i == 1:
                ring_types.append("inner ring")
            else:
                ring_types.append("middle ring")
        return ring_types

    def generate_floor_plan(self, z: float) -> Model:
        model = Model()
        ring_types = self._ring_member_types()
        ring_radii = self._resolved_ring_radii()
        node_grid: List[List[int]] = []

        for radius in ring_radii:
            ring_nodes: List[int] = []
            for k in range(self.n_slices):
                angle = 2.0 * math.pi * k / self.n_slices
                x, y = self.polar_xy(radius, angle)
                ring_nodes.append(model.create_node(x=x, y=y, z=z))
            node_grid.append(ring_nodes)

        for i, ring_nodes in enumerate(node_grid):
            member_type = ring_types[i]
            section = SECTIONS[member_type]
            for k in range(self.n_slices):
                ni = ring_nodes[k]
                nj = ring_nodes[(k + 1) % self.n_slices]
                model.create_line(ni=ni, nj=nj, section=section, member_type=member_type)

        for k in range(self.n_slices):
            for i in range(len(node_grid) - 1):
                ni = node_grid[i][k]
                nj = node_grid[i + 1][k]
                model.create_line(ni=ni, nj=nj, section=SECTIONS["nerve"], member_type="nerve")

        return model

    def generate(self) -> Model:
        ordered_floors = self._validate_geometry()
        model = Model()
        level_node_map: Dict[float, Dict[int, int]] = {}

        template_plan = self.generate_floor_plan(z=ordered_floors[0][1])

        ground_node_map: Dict[int, int] = {}
        for node in template_plan.export_nodes():
            ground_node_map[node["node_id"]] = model.create_node(
                x=node["x"], y=node["y"], z=0.0
            )
        level_node_map[0.0] = ground_node_map

        for _, z in ordered_floors:
            floor_node_map: Dict[int, int] = {}
            for node in template_plan.export_nodes():
                floor_node_map[node["node_id"]] = model.create_node(
                    x=node["x"], y=node["y"], z=z
                )
            level_node_map[z] = floor_node_map

            for line in template_plan.export_lines():
                model.create_line(
                    ni=floor_node_map[line["Ni"]],
                    nj=floor_node_map[line["Nj"]],
                    section=line["section"],
                    member_type=line["type"],
                )

        previous_z = 0.0
        for _, current_z in ordered_floors:
            previous_nodes = level_node_map[previous_z]
            current_nodes = level_node_map[current_z]
            for base_node_id in sorted(previous_nodes):
                model.create_line(
                    ni=previous_nodes[base_node_id],
                    nj=current_nodes[base_node_id],
                    section=SECTIONS["column"],
                    member_type="column",
                )
            previous_z = current_z

        return model


if __name__ == "__main__":
    building = RadialBuilding(core_diameter=16)
    model = building.generate()

    print("Cross sections:")
    print(SECTIONS)
    print()

    print("Floor levels:")
    print(building.floor_levels)
    print()

    print(f"Total nodes: {len(model.nodes)}")
    print(f"Total lines: {len(model.lines)}")
    print()

    print("First 5 nodes:")
    for node in model.export_nodes()[:5]:
        print(node)

    print()
    print("First 5 lines:")
    for line in model.export_lines()[:5]:
        print(line)
