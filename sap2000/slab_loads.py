from typing import Any, Dict, Iterable, List, Tuple

from sap2000.core import _parse_getnamelist_result
from sap2000.load_cases import (
    CSI_LOADPATTERN_DEAD,
    CSI_LOADPATTERN_LIVE,
    ensure_load_pattern,
    recreate_linear_additive_combo,
    recreate_static_linear_case_from_pattern,
)

CSI_ITEMTYPE_OBJECTS = 0
CSI_DIR_GLOBAL_Z = 6
CSI_DIR_GRAVITY = 10


def get_all_area_names(SapModel) -> List[str]:
    try:
        res = SapModel.AreaObj.GetNameList(0, [])
        names, ret = _parse_getnamelist_result(res)
        if ret != 0:
            raise RuntimeError(f"AreaObj.GetNameList failed (ret={ret})")
        return names
    except Exception:
        res = SapModel.AreaObj.GetNameList()
        names, ret = _parse_getnamelist_result(res)
        if ret != 0:
            raise RuntimeError(f"AreaObj.GetNameList failed (ret={ret})")
        return names


def assign_uniform_area_load(
    SapModel,
    area_name: str,
    load_pattern_name: str,
    value: float,
    direction: int = CSI_DIR_GLOBAL_Z,
    coordinate_system: str = "Global",
    replace: bool = True,
) -> None:
    result = SapModel.AreaObj.SetLoadUniform(
        area_name,
        load_pattern_name,
        float(value),
        int(direction),
        bool(replace),
        coordinate_system,
        CSI_ITEMTYPE_OBJECTS,
    )
    ret = result[0] if isinstance(result, tuple) else result
    if ret != 0:
        raise RuntimeError(
            f"AreaObj.SetLoadUniform failed for area {area_name}, "
            f"pattern {load_pattern_name} (ret={ret})"
        )


def assign_uniform_area_load_to_areas(
    SapModel,
    area_names: Iterable[str],
    load_pattern_name: str,
    value: float,
    direction: int = CSI_DIR_GLOBAL_Z,
    coordinate_system: str = "Global",
    replace: bool = True,
) -> List[str]:
    assigned: List[str] = []
    for area_name in area_names:
        assign_uniform_area_load(
            SapModel,
            area_name=area_name,
            load_pattern_name=load_pattern_name,
            value=value,
            direction=direction,
            coordinate_system=coordinate_system,
            replace=replace,
        )
        assigned.append(str(area_name))
    return assigned


def define_basic_slab_gravity_loading(
    SapModel,
    slab_area_names: Iterable[str],
    dead_uniform_load: float,
    live_uniform_load: float,
    dead_self_weight_multiplier: float = 0.0,
    dead_pattern_name: str = "DEAD",
    live_pattern_name: str = "LIVE",
    dead_case_name: str = "DEAD",
    live_case_name: str = "LIVE",
    dead_combo_name: str = "COMBO_DEAD",
    live_combo_name: str = "COMBO_LIVE",
) -> Dict[str, Any]:
    slab_area_names = list(slab_area_names)

    ensure_load_pattern(
        SapModel,
        name=dead_pattern_name,
        load_pattern_type=CSI_LOADPATTERN_DEAD,
        self_weight_multiplier=dead_self_weight_multiplier,
        add_analysis_case=False,
    )
    ensure_load_pattern(
        SapModel,
        name=live_pattern_name,
        load_pattern_type=CSI_LOADPATTERN_LIVE,
        self_weight_multiplier=0.0,
        add_analysis_case=False,
    )

    recreate_static_linear_case_from_pattern(
        SapModel,
        case_name=dead_case_name,
        pattern_name=dead_pattern_name,
        scale_factor=1.0,
    )
    recreate_static_linear_case_from_pattern(
        SapModel,
        case_name=live_case_name,
        pattern_name=live_pattern_name,
        scale_factor=1.0,
    )

    recreate_linear_additive_combo(
        SapModel,
        combo_name=dead_combo_name,
        case_scale_factors={dead_case_name: 1.0},
    )
    recreate_linear_additive_combo(
        SapModel,
        combo_name=live_combo_name,
        case_scale_factors={live_case_name: 1.0},
    )

    assigned_dead = assign_uniform_area_load_to_areas(
        SapModel,
        area_names=slab_area_names,
        load_pattern_name=dead_pattern_name,
        value=dead_uniform_load,
        direction=CSI_DIR_GLOBAL_Z,
        coordinate_system="Global",
        replace=True,
    )
    assigned_live = assign_uniform_area_load_to_areas(
        SapModel,
        area_names=slab_area_names,
        load_pattern_name=live_pattern_name,
        value=live_uniform_load,
        direction=CSI_DIR_GLOBAL_Z,
        coordinate_system="Global",
        replace=True,
    )

    return {
        "patterns": [dead_pattern_name, live_pattern_name],
        "cases": [dead_case_name, live_case_name],
        "combos": [dead_combo_name, live_combo_name],
        "dead_loaded_areas": assigned_dead,
        "live_loaded_areas": assigned_live,
    }
