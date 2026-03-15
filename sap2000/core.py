import json
from pathlib import Path
from typing import Any, Dict, List, Tuple
import pythoncom
import win32com.client as win32
from win32com.client import VARIANT

from geometry.model import Model

SAP_PROGID = "CSI.SAP2000.API.SapObject"

# -------------------- Session (attach) --------------------
class Sap2000Session:
   def __init__(self):
       self.helper = None
       self.SapObject = None
       self.SapModel = None
   def __enter__(self):
       pythoncom.CoInitialize()
       try:
           # Avoid EnsureDispatch; use late-binding to reduce gen_py variant issues
           self.helper = win32.Dispatch("SAP2000v1.Helper")
           self.SapObject = self.helper.GetObject(SAP_PROGID)
           if self.SapObject is None:
               raise RuntimeError(
                   "Could not attach. In SAP2000 use: Tools -> Set as active instance for API. "
                   "Also ensure SAP2000 and Python run with the same admin level and are 64-bit."
               )
           self.SapModel = self.SapObject.SapModel
           if self.SapModel is None:
               raise RuntimeError("Attached SapObject has SapModel=None.")
           return self
       except Exception:
           pythoncom.CoUninitialize()
           raise
   def __exit__(self, exc_type, exc, tb):
       try:
           self.SapModel = None
           self.SapObject = None
           self.helper = None
       finally:
           pythoncom.CoUninitialize()

# -------------------- Robust COM return parsing --------------------
def _parse_getnamelist_result(res: Any) -> Tuple[List[str], int]:
   """
   SAP2000 OAPI 'GetNameList' order differs across wrappers.
   We accept any of these common patterns:
     (NumberNames:int, Names:list, ret:int)
     (ret:int, NumberNames:int, Names:list)
     (NumberNames:int, ret:int, Names:list)
     (Names:list, NumberNames:int, ret:int)  (rare)
   Returns: (names, ret)
   """
   if not isinstance(res, tuple):
       raise RuntimeError(f"GetNameList returned non-tuple: {type(res)} {res}")
   ints = [v for v in res if isinstance(v, int)]
   lists = [v for v in res if isinstance(v, (list, tuple))]
   # Heuristic: ret is usually the last int or the smallest int (often 0)
   ret = None
   if ints:
       # Prefer a 0/negative/low number as ret if present
       if 0 in ints:
           ret = 0
       else:
           ret = ints[-1]
   names = None
   if lists:
       # Prefer the longest list/tuple as the names list
       names = max(lists, key=lambda x: len(x))
   if ret is None or names is None:
       raise RuntimeError(f"Could not parse GetNameList return: {res}")
   return [str(n) for n in list(names)], int(ret)

def get_all_point_names(SapModel) -> List[str]:
   # Try explicit OUT args first
   try:
       res = SapModel.PointObj.GetNameList(0, [])
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"PointObj.GetNameList failed (ret={ret})")
       return names
   except Exception:
       # Fallback: call with no args (some bindings support it)
       res = SapModel.PointObj.GetNameList()
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"PointObj.GetNameList failed (ret={ret})")
       return names

def get_all_load_combos(SapModel) -> List[str]:
   try:
       res = SapModel.RespCombo.GetNameList(0, [])
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"RespCombo.GetNameList failed (ret={ret})")
       return names
   except Exception:
       res = SapModel.RespCombo.GetNameList()
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"RespCombo.GetNameList failed (ret={ret})")
       return names

def get_all_load_cases(SapModel) -> List[str]:
   try:
       res = SapModel.LoadCases.GetNameList(0, [])
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"LoadCases.GetNameList failed (ret={ret})")
       return names
   except Exception:
       res = SapModel.LoadCases.GetNameList()
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"LoadCases.GetNameList failed (ret={ret})")
       return names

# -------------------- Geometry + supports --------------------
def get_point_coords(SapModel, point_name: str) -> Tuple[float, float, float]:
   # GetCoordCartesian returns (Z, X, Y, ret) based on observed behavior
   # The coordinate order is rotated from what we'd expect
   result = SapModel.PointObj.GetCoordCartesian(point_name, 0, 0, 0)

   if not isinstance(result, tuple) or len(result) != 4:
       raise RuntimeError(f"GetCoordCartesian returned unexpected format: {result}")

   # Unpack: API returns (z, x, y, ret) - coordinates are rotated!
   z_sap, x_sap, y_sap, ret = result

   if ret != 0:
       raise RuntimeError(f"GetCoordCartesian({point_name}) failed (ret={ret})")

   # Return in correct order: (x, y, z)
   return float(x_sap), float(y_sap), float(z_sap)

def get_point_restraint(SapModel, point_name: str) -> List[int]:
   """
   Handles common variants:
     (restraint, ret)  where restraint is 6-length array
     (ret, restraint)
   """
   try:
       res = SapModel.PointObj.GetRestraint(point_name, [0, 0, 0, 0, 0, 0])
   except Exception:
       res = SapModel.PointObj.GetRestraint(point_name)
   if not isinstance(res, tuple):
       raise RuntimeError(f"GetRestraint returned non-tuple: {type(res)} {res}")
   # Find the restraint array and the ret int
   restraint = None
   ret = None
   for v in res:
       if isinstance(v, int):
           # ret often 0/1; keep last int
           ret = v
       if isinstance(v, (list, tuple)) and len(v) == 6:
           restraint = v
   if restraint is None or ret is None:
       raise RuntimeError(f"Could not parse GetRestraint return: {res}")
   if int(ret) != 0:
       raise RuntimeError(f"GetRestraint({point_name}) failed (ret={ret})")
   return [int(v) for v in list(restraint)]

def get_support_nodes(SapModel) -> List[Dict[str, Any]]:
   supports: List[Dict[str, Any]] = []
   for pt in get_all_point_names(SapModel):
       r = get_point_restraint(SapModel, pt)
       if any(r):
           x, y, z = get_point_coords(SapModel, pt)
           supports.append(
               {
                   "Joint": pt,
                   "X": x,
                   "Y": y,
                   "Z": z,
                   "Restraint": {"U1": r[0], "U2": r[1], "U3": r[2], "R1": r[3], "R2": r[4], "R3": r[5]},
               }
           )
   return supports

# -------------------- Results (combos + reactions) --------------------
def run_analysis(SapModel) -> None:
   ret = SapModel.Analyze.RunAnalysis()
   if ret != 0:
       raise RuntimeError(f"Analyze.RunAnalysis failed (ret={ret})")


def ensure_blank_model(SapModel, units: int = 6) -> None:
   """
   Initialize a blank SAP2000 model.

   The default `units=6` corresponds to kN, m, C in CSI products.
   """
   ret = SapModel.InitializeNewModel(units)
   if ret != 0:
       raise RuntimeError(f"InitializeNewModel failed (ret={ret})")
   ret = SapModel.File.NewBlank()
   if ret != 0:
       raise RuntimeError(f"File.NewBlank failed (ret={ret})")


def define_steel_material(
   SapModel,
   material_name: str = "S355",
   e_modulus: float = 210000000.0,
   poisson_ratio: float = 0.3,
   thermal_coefficient: float = 1.2e-5,
) -> str:
   """
   Create a steel material if it does not already exist.

   Defaults assume SI units with E in kN/m^2 when the model uses kN-m-C.
   """
   MATERIAL_STEEL = 1
   ret = SapModel.PropMaterial.SetMaterial(material_name, MATERIAL_STEEL)
   if ret != 0:
       raise RuntimeError(f"PropMaterial.SetMaterial failed for {material_name} (ret={ret})")

   ret = SapModel.PropMaterial.SetMPIsotropic(
       material_name,
       e_modulus,
       poisson_ratio,
       thermal_coefficient,
   )
   if ret != 0:
       raise RuntimeError(f"PropMaterial.SetMPIsotropic failed for {material_name} (ret={ret})")
   return material_name


def define_concrete_material(
   SapModel,
   material_name: str = "C30",
   e_modulus: float = 25000000.0,
   poisson_ratio: float = 0.2,
   thermal_coefficient: float = 1.0e-5,
) -> str:
   MATERIAL_CONCRETE = 2
   ret = SapModel.PropMaterial.SetMaterial(material_name, MATERIAL_CONCRETE)
   if ret != 0:
       raise RuntimeError(f"PropMaterial.SetMaterial failed for {material_name} (ret={ret})")

   ret = SapModel.PropMaterial.SetMPIsotropic(
       material_name,
       e_modulus,
       poisson_ratio,
       thermal_coefficient,
   )
   if ret != 0:
       raise RuntimeError(f"PropMaterial.SetMPIsotropic failed for {material_name} (ret={ret})")
   return material_name


def define_rectangular_frame_section(
   SapModel,
   section_name: str = "STEEL_RECT",
   material_name: str = "S355",
   depth: float = 0.30,
   width: float = 0.20,
) -> str:
   ret = SapModel.PropFrame.SetRectangle(section_name, material_name, depth, width)
   if ret != 0:
       raise RuntimeError(f"PropFrame.SetRectangle failed for {section_name} (ret={ret})")
   return section_name


def define_slab_area_section(
   SapModel,
   section_name: str = "SLAB150",
   material_name: str = "C30",
   thickness: float = 0.15,
) -> str:
   SHELL_TYPE_SHELL_THIN = 1
   color = 0

   if hasattr(SapModel.PropArea, "SetShell_1"):
       ret = SapModel.PropArea.SetShell_1(
           section_name,
           SHELL_TYPE_SHELL_THIN,
           True,
           material_name,
           color,
           thickness,
           thickness,
       )
       if ret != 0:
           raise RuntimeError(f"PropArea.SetShell_1 failed for {section_name} (ret={ret})")
       return section_name

   if hasattr(SapModel.PropArea, "SetShell"):
       ret = SapModel.PropArea.SetShell(
           section_name,
           SHELL_TYPE_SHELL_THIN,
           material_name,
           color,
           thickness,
           thickness,
       )
       if ret != 0:
           raise RuntimeError(f"PropArea.SetShell failed for {section_name} (ret={ret})")
       return section_name

   available_methods = [name for name in dir(SapModel.PropArea) if "Shell" in name or "Slab" in name]
   raise RuntimeError(
       "SAP2000 did not expose a supported shell area-property initializer on SapModel.PropArea. "
       f"Available related methods: {available_methods}"
   )


def define_i_frame_section(
   SapModel,
   section_name: str,
   material_name: str,
   depth: float,
   flange_width: float,
   web_thickness: float,
   flange_thickness: float,
) -> str:
   """
   Define a symmetric I-section suitable for UB/UC-like custom sections.
   """
   ret = SapModel.PropFrame.SetISection(
       section_name,
       material_name,
       depth,
       flange_width,
       flange_thickness,
       web_thickness,
       flange_width,
       flange_thickness,
   )
   if ret != 0:
       raise RuntimeError(f"PropFrame.SetISection failed for {section_name} (ret={ret})")
   return section_name


def _section_database_candidates(section_label: str) -> List[str]:
   normalized = section_label.strip().upper()
   if normalized.startswith(("UB", "UC")):
       return ["BSShapes2006.pro", "BSShapes.pro"]
   raise ValueError(f"Unsupported database-backed section label: {section_label}")


def get_all_frame_section_names(SapModel) -> List[str]:
   try:
       res = SapModel.PropFrame.GetNameList(0, [])
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"PropFrame.GetNameList failed (ret={ret})")
       return names
   except Exception:
       res = SapModel.PropFrame.GetNameList()
       names, ret = _parse_getnamelist_result(res)
       if ret != 0:
           raise RuntimeError(f"PropFrame.GetNameList failed (ret={ret})")
       return names


def _database_section_label(section_label: str) -> str:
   return section_label.strip().replace(" ", "")


def import_frame_section_from_label(
   SapModel,
   section_label: str,
   material_name: str = "S355",
) -> str:
   """
   Import a frame section from a CSI property database using the compact
   no-space label used by the generated model, e.g. 'UB203X133X25'.
   """
   section_name = _database_section_label(section_label)
   existing_section_names = set(get_all_frame_section_names(SapModel))
   if section_name in existing_section_names:
       return section_name

   failures: List[str] = []
   for database_file in _section_database_candidates(section_label):
       ret = SapModel.PropFrame.ImportProp(
           section_name,
           material_name,
           database_file,
           section_name,
       )
       if ret == 0:
           return section_name
       failures.append(f"{database_file}:{section_name} ret={ret}")
   raise RuntimeError(
       f"PropFrame.ImportProp failed for section {section_label}. "
       f"Tried: {', '.join(failures)}"
   )


def import_model_frame_sections(
   SapModel,
   model: Model,
   material_name: str = "S355",
   custom_sections: Dict[str, Dict[str, float]] | None = None,
) -> Dict[str, str]:
   imported_sections: Dict[str, str] = {}
   for line in model.export_lines():
       section_label = str(line["section"])
       if section_label in imported_sections:
           continue
       if custom_sections and section_label in custom_sections:
           props = custom_sections[section_label]
           imported_sections[section_label] = define_i_frame_section(
               SapModel,
               section_name=section_label,
               material_name=material_name,
               depth=props["depth"],
               flange_width=props["flange_width"],
               web_thickness=props["web_thickness"],
               flange_thickness=props["flange_thickness"],
           )
           continue
       imported_sections[section_label] = import_frame_section_from_label(
           SapModel,
           section_label=section_label,
           material_name=material_name,
       )
   return imported_sections


def create_points_from_model(SapModel, model: Model) -> Dict[int, str]:
   """
   Create SAP2000 point objects using the model's node ids as names.

   Returns a mapping of local node id -> SAP2000 point object name.
   """
   point_names: Dict[int, str] = {}
   for node in model.export_nodes():
       point_name = str(node["node_id"])
       result = SapModel.PointObj.AddCartesian(
           node["x"],
           node["y"],
           node["z"],
           " ",
           point_name,
       )
       if not isinstance(result, tuple) or len(result) < 2:
           raise RuntimeError(f"PointObj.AddCartesian returned unexpected result for node {point_name}: {result}")
       ret, sap_name = result[0], result[1]
       if ret != 0:
           raise RuntimeError(f"PointObj.AddCartesian failed for node {point_name} (ret={ret})")
       point_names[node["node_id"]] = str(sap_name or point_name)
   return point_names


def _parse_add_area_result(result: Any) -> Tuple[int, str]:
   if not isinstance(result, tuple):
       raise RuntimeError(f"AreaObj.AddByPoint returned non-tuple: {type(result)} {result}")

   ret = None
   area_name = ""
   for value in result:
       if isinstance(value, int):
           ret = value
       elif isinstance(value, str):
           area_name = value

   if ret is None:
       raise RuntimeError(f"Could not parse AreaObj.AddByPoint return: {result}")
   return int(ret), str(area_name)


def create_slab_from_4_points(
   SapModel,
   area_name: str,
   p1: str,
   p2: str,
   p3: str,
   p4: str,
   section_name: str,
) -> str:
   result = SapModel.AreaObj.AddByPoint(
       4,
       [p1, p2, p3, p4],
       area_name,
       section_name,
       area_name,
   )
   ret, sap_name = _parse_add_area_result(result)
   if ret != 0:
       raise RuntimeError(f"AreaObj.AddByPoint failed for {area_name} (ret={ret})")
   return sap_name or area_name


def create_slab_from_4_node_ids(
   SapModel,
   area_name: str,
   node_ids: List[int],
   point_names: Dict[int, str],
   section_name: str,
) -> str:
   if len(node_ids) != 4:
       raise ValueError(f"Slab {area_name} must have exactly 4 node ids, got {len(node_ids)}")

   p1, p2, p3, p4 = [point_names[node_id] for node_id in node_ids]
   return create_slab_from_4_points(
       SapModel,
       area_name=area_name,
       p1=p1,
       p2=p2,
       p3=p3,
       p4=p4,
       section_name=section_name,
   )


def create_areas_from_model(
   SapModel,
   model: Model,
   point_names: Dict[int, str],
) -> Dict[int, str]:
   area_names: Dict[int, str] = {}
   for area in model.export_areas():
       area_id = int(area["area_id"])
       sap_name = create_slab_from_4_node_ids(
           SapModel,
           area_name=str(area_id),
           node_ids=list(area["nodes"]),
           point_names=point_names,
           section_name=str(area["section"]),
       )
       area_names[area_id] = sap_name
   return area_names


def create_frames_from_model(
   SapModel,
   model: Model,
   point_names: Dict[int, str],
   frame_sections: Dict[str, str],
) -> Dict[int, str]:
   """
   Create SAP2000 frame objects using the model's line ids as names.

   Frame section assignments come directly from `line["section"]`.
   """
   frame_names: Dict[int, str] = {}
   for line in model.export_lines():
       frame_name = str(line["line_id"])
       section_name = frame_sections[str(line["section"])]
       result = SapModel.FrameObj.AddByPoint(
           point_names[line["Ni"]],
           point_names[line["Nj"]],
           frame_name,
           section_name,
           "Global",
       )
       if not isinstance(result, tuple) or len(result) < 2:
           raise RuntimeError(f"FrameObj.AddByPoint returned unexpected result for frame {frame_name}: {result}")
       ret, sap_name = result[0], result[1]
       if ret != 0:
           raise RuntimeError(f"FrameObj.AddByPoint failed for frame {frame_name} (ret={ret})")
       frame_names[line["line_id"]] = str(sap_name or frame_name)
   return frame_names


def import_structural_model(
   SapModel,
   model: Model,
   material_name: str = "S355",
   custom_sections: Dict[str, Dict[str, float]] | None = None,
   concrete_material_name: str = "C30",
   slab_thickness: float = 0.15,
   initialize_blank: bool = False,
   units: int = 6,
) -> Dict[str, Any]:
   """
   Define a steel material, import the model's steel sections from the CSI
   section database, optionally define custom I-sections, then create all
   points and frame objects.
   """
   if initialize_blank:
       ensure_blank_model(SapModel, units=units)

   define_steel_material(SapModel, material_name=material_name)
   imported_sections = import_model_frame_sections(
       SapModel,
       model,
       material_name=material_name,
       custom_sections=custom_sections,
   )
   point_names = create_points_from_model(SapModel, model)
   frame_names = create_frames_from_model(
       SapModel,
       model,
       point_names=point_names,
       frame_sections=imported_sections,
   )
   area_names: Dict[int, str] = {}
   if model.export_areas():
       define_concrete_material(SapModel, material_name=concrete_material_name)
       for area_section in sorted({str(area["section"]) for area in model.export_areas()}):
           define_slab_area_section(
               SapModel,
               section_name=area_section,
               material_name=concrete_material_name,
               thickness=slab_thickness,
           )
       area_names = create_areas_from_model(
           SapModel,
           model,
           point_names=point_names,
       )
   return {
       "material_name": material_name,
       "sections": imported_sections,
       "points": point_names,
       "frames": frame_names,
       "areas": area_names,
   }

def select_results_output(SapModel, name: str) -> str:
   ret = SapModel.Results.Setup.DeselectAllCasesAndCombosForOutput()
   if ret != 0:
       raise RuntimeError(f"DeselectAllCasesAndCombosForOutput failed (ret={ret})")
   ret_case = SapModel.Results.Setup.SetCaseSelectedForOutput(name)
   if ret_case == 0:
       return "case"
   ret_combo = SapModel.Results.Setup.SetComboSelectedForOutput(name)
   if ret_combo == 0:
       return "combo"
   raise RuntimeError(f"Could not select '{name}' as case (ret={ret_case}) or combo (ret={ret_combo}).")

def get_joint_reaction_first_row(SapModel, joint_name: str) -> Dict[str, Any]:
   # ItemTypeElm: 0 = ObjectElm (for point object name)
   # With early binding (gen_py), we must pass pythoncom.Missing for output parameters
   result = SapModel.Results.JointReact(
       joint_name,
       0,  # ItemTypeElm
       pythoncom.Missing,  # NumberResults (OUT)
       pythoncom.Missing,  # Obj (OUT)
       pythoncom.Missing,  # Elm (OUT)
       pythoncom.Missing,  # LoadCase (OUT)
       pythoncom.Missing,  # StepType (OUT)
       pythoncom.Missing,  # StepNum (OUT)
       pythoncom.Missing,  # F1 (OUT)
       pythoncom.Missing,  # F2 (OUT)
       pythoncom.Missing,  # F3 (OUT)
       pythoncom.Missing,  # M1 (OUT)
       pythoncom.Missing,  # M2 (OUT)
       pythoncom.Missing   # M3 (OUT)
   )

   if not isinstance(result, tuple):
       raise RuntimeError(f"JointReact returned non-tuple: {type(result)}")

   # Actual order from debug: (ret, NumberResults, Obj, Elm, LoadCase, StepType, StepNum, F1, F2, F3, M1, M2, M3)
   # That's 13 elements: 2 ints and 11 arrays
   if len(result) != 13:
       raise RuntimeError(f"Expected 13 elements, got {len(result)}: {result}")

   (
       ret_code,
       number_results,
       obj,
       elm,
       load_case_arr,
       step_type,
       step_num,
       f1,
       f2,
       f3,
       m1,
       m2,
       m3,
   ) = result

   if ret_code != 0:
       raise RuntimeError(f"JointReact({joint_name}) failed (ret={ret_code})")

   # Return first row if available
   if not load_case_arr or len(load_case_arr) == 0:
       return {
           "ResultName": "",
           "StepType": "",
           "StepNum": 0.0,
           "F1": 0.0,
           "F2": 0.0,
           "F3": 0.0,
           "M1": 0.0,
           "M2": 0.0,
           "M3": 0.0,
       }

   i = 0
   return {
       "ResultName": str(load_case_arr[i]),
       "StepType": str(step_type[i]),
       "StepNum": float(step_num[i]),
       "F1": float(f1[i]),
       "F2": float(f2[i]),
       "F3": float(f3[i]),
       "M1": float(m1[i]),
       "M2": float(m2[i]),
       "M3": float(m3[i]),
   }

def get_support_reactions_all_combos(SapModel) -> Tuple[List[Dict[str, Any]], Dict[str, Dict[str, Any]]]:
   """
   Returns:
       - List of support nodes with coordinates
       - Dict of reactions organized by joint name, then by load combo/case
   """
   supports = get_support_nodes(SapModel)

   # Get all load combinations
   names: List[str] = []
   names.extend(get_all_load_combos(SapModel))

   # Organize reactions by joint, then by load combo
   reactions_by_joint: Dict[str, Dict[str, Any]] = {}

   for s in supports:
       j = s["Joint"]
       reactions_by_joint[j] = {}

   for name in names:
       selected_type = select_results_output(SapModel, name)

       for s in supports:
           j = s["Joint"]
           r = get_joint_reaction_first_row(SapModel, j)

           # Store reaction for this joint and load combo
           reactions_by_joint[j][name] = {
               "Type": selected_type,
               "StepType": r["StepType"],
               "StepNum": r["StepNum"],
               "F1": r["F1"],
               "F2": r["F2"],
               "F3": r["F3"],
               "M1": r["M1"],
               "M2": r["M2"],
               "M3": r["M3"],
           }

   return supports, reactions_by_joint

def save_json(payload: Dict[str, Any], out_path: str | Path) -> None:
   out_path = Path(out_path).resolve()
   out_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

# -------------------- Main --------------------
if __name__ == "__main__":
   COORDS_FILE = "support_nodes_coordinates.json"
   REACTIONS_FILE = "support_reactions_by_node.json"

   with Sap2000Session() as sap:
       run_analysis(sap.SapModel)
       supports, reactions = get_support_reactions_all_combos(sap.SapModel)

       # Save support coordinates
       save_json(supports, COORDS_FILE)
       print(f"\nSupport nodes: {len(supports)}")
       print(f"Wrote coordinates to: {Path(COORDS_FILE).resolve()}")

       # Save reactions by node
       save_json(reactions, REACTIONS_FILE)
       print(f"\nReactions for {len(reactions)} nodes")
       if reactions:
           first_node = list(reactions.keys())[0]
           num_combos = len(reactions[first_node])
           print(f"Load combos/cases per node: {num_combos}")
       print(f"Wrote reactions to: {Path(REACTIONS_FILE).resolve()}")
