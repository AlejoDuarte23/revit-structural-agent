from typing import Dict, List

import pythoncom
from win32com.client import VARIANT

from sap2000.core import _parse_getnamelist_result, get_all_load_cases, get_all_load_combos

CSI_LOADPATTERN_DEAD = 1
CSI_LOADPATTERN_LIVE = 3

CSI_COMBO_LINEAR_ADDITIVE = 0
CSI_CNAME_LOADCASE = 0


def get_all_load_patterns(SapModel) -> List[str]:
    try:
        res = SapModel.LoadPatterns.GetNameList(0, [])
        names, ret = _parse_getnamelist_result(res)
        if ret != 0:
            raise RuntimeError(f"LoadPatterns.GetNameList failed (ret={ret})")
        return names
    except Exception:
        res = SapModel.LoadPatterns.GetNameList()
        names, ret = _parse_getnamelist_result(res)
        if ret != 0:
            raise RuntimeError(f"LoadPatterns.GetNameList failed (ret={ret})")
        return names


def ensure_load_pattern(
    SapModel,
    name: str,
    load_pattern_type: int,
    self_weight_multiplier: float = 0.0,
    add_analysis_case: bool = False,
) -> str:
    existing = set(get_all_load_patterns(SapModel))
    if name in existing:
        return name

    ret = SapModel.LoadPatterns.Add(
        name,
        load_pattern_type,
        float(self_weight_multiplier),
        bool(add_analysis_case),
    )
    if ret != 0:
        raise RuntimeError(f"LoadPatterns.Add failed for {name} (ret={ret})")
    return name


def recreate_static_linear_case_from_pattern(
    SapModel,
    case_name: str,
    pattern_name: str,
    scale_factor: float = 1.0,
) -> str:
    ret = SapModel.LoadCases.StaticLinear.SetCase(case_name)
    if ret != 0:
        raise RuntimeError(f"LoadCases.StaticLinear.SetCase failed for {case_name} (ret={ret})")

    ret = SapModel.LoadCases.StaticLinear.SetLoads(
        case_name,
        1,
        ["Load"],
        [pattern_name],
        [float(scale_factor)],
    )
    if ret != 0:
        raise RuntimeError(
            f"LoadCases.StaticLinear.SetLoads failed for case {case_name} "
            f"with pattern {pattern_name} (ret={ret})"
        )
    return case_name


def recreate_linear_additive_combo(
    SapModel,
    combo_name: str,
    case_scale_factors: Dict[str, float],
) -> str:
    existing_case_names = set(get_all_load_cases(SapModel))

    if combo_name in existing_case_names:
        raise RuntimeError(
            f"Response combo name {combo_name} conflicts with an existing load case. "
            "Combo names must differ from all load case names."
        )

    existing_combo_names = set(get_all_load_combos(SapModel))
    if combo_name in existing_combo_names:
        ret = SapModel.RespCombo.Delete(combo_name)
        if ret != 0:
            raise RuntimeError(f"RespCombo.Delete failed for {combo_name} (ret={ret})")

    ret = SapModel.RespCombo.Add(combo_name, CSI_COMBO_LINEAR_ADDITIVE)
    if ret != 0:
        raise RuntimeError(f"RespCombo.Add failed for {combo_name} (ret={ret})")

    for case_name, scale_factor in case_scale_factors.items():
        try:
            ret = SapModel.RespCombo.SetCaseList(
                combo_name,
                CSI_CNAME_LOADCASE,
                case_name,
                float(scale_factor),
            )
        except Exception:
            cname_type = VARIANT(pythoncom.VT_BYREF | pythoncom.VT_I4, CSI_CNAME_LOADCASE)
            ret = SapModel.RespCombo.SetCaseList(
                combo_name,
                cname_type,
                case_name,
                float(scale_factor),
            )
        if ret != 0:
            raise RuntimeError(
                f"RespCombo.SetCaseList failed for combo {combo_name}, "
                f"case {case_name}, sf {scale_factor} (ret={ret})"
            )

    return combo_name
