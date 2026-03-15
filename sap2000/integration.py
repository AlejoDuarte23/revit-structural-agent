from typing import Any, Dict, List, Optional

from geometry.model import Model, RadialBuilding
from sap2000.core import Sap2000Session, import_structural_model
from sap2000.slab_loads import define_basic_slab_gravity_loading


def build_radial_geometry(
    diameter: float = 32.0,
    core_diameter: float = 8.0,
    n_slices: int = 20,
    inner_ring_count: int = 2,
    ring_radii: Optional[List[float]] = None,
    floor_levels: Optional[Dict[str, float]] = None,
) -> Model:
    building = RadialBuilding(
        diameter=diameter,
        core_diameter=core_diameter,
        n_slices=n_slices,
        inner_ring_count=inner_ring_count,
        ring_radii=ring_radii,
        floor_levels=floor_levels,
    )
    return building.generate()


def create_radial_building_in_sap2000(
    diameter: float = 32.0,
    core_diameter: float = 8.0,
    n_slices: int = 20,
    inner_ring_count: int = 2,
    ring_radii: Optional[List[float]] = None,
    floor_levels: Optional[Dict[str, float]] = None,
    custom_sections: Optional[Dict[str, Dict[str, float]]] = None,
    initialize_blank: bool = True,
    units: int = 6,
    material_name: str = "S355",
    concrete_material_name: str = "C30",
    slab_thickness: float = 0.15,
    apply_basic_slab_loading: bool = False,
    dead_uniform_load: float = -2.5,
    live_uniform_load: float = -3.0,
    dead_self_weight_multiplier: float = 0.0,
) -> Dict[str, Any]:
    geometry_model = build_radial_geometry(
        diameter=diameter,
        core_diameter=core_diameter,
        n_slices=n_slices,
        inner_ring_count=inner_ring_count,
        ring_radii=ring_radii,
        floor_levels=floor_levels,
    )

    with Sap2000Session() as sap:
        sap_result = import_structural_model(
            sap.SapModel,
            geometry_model,
            material_name=material_name,
            custom_sections=custom_sections,
            concrete_material_name=concrete_material_name,
            slab_thickness=slab_thickness,
            initialize_blank=initialize_blank,
            units=units,
        )

        loading_result: Optional[Dict[str, Any]] = None
        if apply_basic_slab_loading and sap_result.get("areas"):
            loading_result = define_basic_slab_gravity_loading(
                sap.SapModel,
                slab_area_names=list(sap_result["areas"].values()),
                dead_uniform_load=dead_uniform_load,
                live_uniform_load=live_uniform_load,
                dead_self_weight_multiplier=dead_self_weight_multiplier,
            )

        return {
            "geometry": geometry_model,
            "sap2000": sap_result,
            "loading": loading_result,
        }


if __name__ == "__main__":
    result = create_radial_building_in_sap2000(
        apply_basic_slab_loading=False,
    )
    geometry_model = result["geometry"]
    print(f"Created geometry with {len(geometry_model.nodes)} nodes")
    print(f"Created geometry with {len(geometry_model.lines)} lines")
    print(f"Created geometry with {len(geometry_model.areas)} areas")
    print(f"Imported {len(result['sap2000']['points'])} SAP2000 points")
    print(f"Imported {len(result['sap2000']['frames'])} SAP2000 frames")
    print(f"Imported {len(result['sap2000']['areas'])} SAP2000 areas")
