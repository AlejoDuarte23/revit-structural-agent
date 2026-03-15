import math
from typing import Any, Dict, List, Optional, Tuple


class Model:
    def __init__(self) -> None:
        self.current_node_id = 0
        self.current_line_id = 0
        self.current_area_id = 0
        self.nodes: Dict[int, dict] = {}
        self.lines: Dict[int, dict] = {}
        self.areas: Dict[int, dict] = {}

    def create_node_id(self) -> int:
        self.current_node_id += 1
        return self.current_node_id

    def create_line_id(self) -> int:
        self.current_line_id += 1
        return self.current_line_id

    def create_area_id(self) -> int:
        self.current_area_id += 1
        return self.current_area_id

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

    def create_area(
        self,
        node_ids: List[int],
        section: str,
        area_type: str,
        area_id: Optional[int] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> int:
        if len(node_ids) < 3:
            raise ValueError("Area must have at least 3 nodes")
        if any(node_id not in self.nodes for node_id in node_ids):
            raise ValueError("All area nodes must already exist")

        aid = self.create_area_id() if area_id is None else int(area_id)
        if aid in self.areas:
            raise ValueError(f"Area {aid} already exists")

        payload = {
            "area_id": aid,
            "nodes": [int(node_id) for node_id in node_ids],
            "section": str(section),
            "type": str(area_type),
        }
        if metadata:
            payload.update(metadata)

        self.areas[aid] = payload
        self.current_area_id = max(self.current_area_id, aid)
        return aid

    def export_nodes(self) -> List[dict]:
        return [self.nodes[nid] for nid in sorted(self.nodes)]

    def export_lines(self) -> List[dict]:
        return [self.lines[lid] for lid in sorted(self.lines)]

    def export_areas(self) -> List[dict]:
        return [self.areas[aid] for aid in sorted(self.areas)]


SECTIONS = {
    "core ring": "UB 203x133x25",
    "inner ring": "UB 254x146x31",
    "middle ring": "UB 305x165x40",
    "outer ring": "UB 356x171x45",
    "nerve": "UB 203x102x23",
    "column": "UC 254x254x73",
}

AREA_SECTIONS = {
    "slab": "SLAB150",
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
            "floor_1": 4.0,
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

    def _create_ring_nodes(self, model: Model, z: float) -> List[List[int]]:
        ring_radii = self._resolved_ring_radii()
        node_grid: List[List[int]] = []

        for radius in ring_radii:
            ring_nodes: List[int] = []
            for k in range(self.n_slices):
                angle = 2.0 * math.pi * k / self.n_slices
                x, y = self.polar_xy(radius, angle)
                ring_nodes.append(model.create_node(x=x, y=y, z=z))
            node_grid.append(ring_nodes)

        return node_grid

    def _create_ring_and_radial_members(self, model: Model, node_grid: List[List[int]]) -> None:
        ring_types = self._ring_member_types()

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

    def create_floor_slabs(
        self,
        model: Model,
        node_grid: List[List[int]],
        floor_name: str,
        z: float,
    ) -> Dict[int, dict]:
        slabs: Dict[int, dict] = {}
        for ring_index in range(len(node_grid) - 1):
            for slice_index in range(self.n_slices):
                next_slice_index = (slice_index + 1) % self.n_slices
                slab_nodes = [
                    node_grid[ring_index][slice_index],
                    node_grid[ring_index + 1][slice_index],
                    node_grid[ring_index + 1][next_slice_index],
                    node_grid[ring_index][next_slice_index],
                ]
                slab_id = model.create_area(
                    node_ids=slab_nodes,
                    section=AREA_SECTIONS["slab"],
                    area_type="slab",
                    metadata={
                        "floor": floor_name,
                        "z": float(z),
                        "ring_index": ring_index,
                        "slice_index": slice_index,
                    },
                )
                slabs[slab_id] = model.areas[slab_id]
        return slabs

    def _create_columns(
        self,
        model: Model,
        lower_node_grid: List[List[int]],
        upper_node_grid: List[List[int]],
    ) -> None:
        for ring_index in range(len(lower_node_grid)):
            for slice_index in range(self.n_slices):
                model.create_line(
                    ni=lower_node_grid[ring_index][slice_index],
                    nj=upper_node_grid[ring_index][slice_index],
                    section=SECTIONS["column"],
                    member_type="column",
                )

    def generate_floor_plan(self, z: float) -> Model:
        model = Model()
        node_grid = self._create_ring_nodes(model, z=z)
        self._create_ring_and_radial_members(model, node_grid)
        self.create_floor_slabs(model, node_grid, floor_name=f"z_{z}", z=z)
        return model

    def generate(self) -> Model:
        ordered_floors = self._validate_geometry()
        model = Model()
        lower_node_grid = self._create_ring_nodes(model, z=0.0)

        for floor_name, z in ordered_floors:
            floor_node_grid = self._create_ring_nodes(model, z=z)
            self._create_ring_and_radial_members(model, floor_node_grid)
            self.create_floor_slabs(model, floor_node_grid, floor_name=floor_name, z=z)
            self._create_columns(model, lower_node_grid, floor_node_grid)
            lower_node_grid = floor_node_grid

        return model


if __name__ == "__main__":
    building = RadialBuilding()
    model = building.generate()

    print("Cross sections:")
    print(SECTIONS)
    print()

    print("Floor levels:")
    print(building.floor_levels)
    print()

    print(f"Total nodes: {len(model.nodes)}")
    print(f"Total lines: {len(model.lines)}")
    print(f"Total areas: {len(model.areas)}")
    print()

    print("First 5 nodes:")
    for node in model.export_nodes()[:5]:
        print(node)

    print()
    print("First 5 lines:")
    for line in model.export_lines()[:5]:
        print(line)

    print()
    print("First 5 areas:")
    for area in model.export_areas()[:5]:
        print(area)
