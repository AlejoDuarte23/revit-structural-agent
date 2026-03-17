from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List, Optional

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from geometry.model import Model


class SourceRefModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    kind: str
    revit_element_id: Optional[int] = None
    revit_unique_id: Optional[str] = None


class SupportSpecModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    restraint: List[int]

    @field_validator("restraint", mode="before")
    @classmethod
    def validate_restraint(cls, value: Any) -> List[int]:
        if isinstance(value, dict):
            keys = ("U1", "U2", "U3", "R1", "R2", "R3")
            return [int(value.get(key, 0)) for key in keys]

        if isinstance(value, (list, tuple)):
            if len(value) != 6:
                raise ValueError("support restraint must contain 6 values")
            return [int(item) for item in value]

        raise ValueError("support restraint must be a 6-item list or a U1..R3 object")


class NodeMetadataModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    coord_key: Optional[str] = None
    source_refs: List[SourceRefModel] = Field(default_factory=list)
    support: Optional[SupportSpecModel] = None


class LineMetadataModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    revit_element_id: Optional[int] = None
    revit_unique_id: Optional[str] = None
    structural_role: Optional[str] = None
    material_name: Optional[str] = None
    section_shape: Optional[str] = None
    cross_section_rotation_deg: Optional[float] = None
    physical_element_id: Optional[int] = None
    physical_unique_id: Optional[str] = None


class ForceVectorModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    x: float = 0.0
    y: float = 0.0
    z: float = 0.0


class AreaUniformLoadModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    kind: str
    load_case_name: str
    load_category_name: Optional[str] = None
    load_nature_name: Optional[str] = None
    is_hosted: Optional[bool] = None
    is_constrained_on_host: Optional[bool] = None
    is_projected: Optional[bool] = None
    orient_to: Optional[str] = None
    force_vector_global_kn_per_m2: ForceVectorModel
    num_reference_points: Optional[int] = None
    host_element_id: Optional[int] = None
    host_unique_id: Optional[str] = None

    @field_validator("kind")
    @classmethod
    def validate_kind(cls, value: str) -> str:
        if value != "area_uniform":
            raise ValueError("only 'area_uniform' area loads are supported")
        return value


class AreaMetadataModel(BaseModel):
    model_config = ConfigDict(extra="allow")

    revit_element_id: Optional[int] = None
    revit_unique_id: Optional[str] = None
    structural_role: Optional[str] = None
    material_name: Optional[str] = None
    thickness_mm: Optional[float] = None
    physical_element_id: Optional[int] = None
    physical_unique_id: Optional[str] = None
    openings: List[Dict[str, Any]] = Field(default_factory=list)
    loads: List[AreaUniformLoadModel] = Field(default_factory=list)


class ExportNodeModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    node_id: int
    x: float
    y: float
    z: float
    metadata: Optional[NodeMetadataModel] = None


class ExportLineModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    line_id: int
    Ni: int
    Nj: int
    section: str
    type: str
    metadata: Optional[LineMetadataModel] = None


class ExportAreaModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    area_id: int
    nodes: List[int]
    section: str
    type: str
    metadata: Optional[AreaMetadataModel] = None

    @field_validator("nodes")
    @classmethod
    def validate_nodes(cls, value: List[int]) -> List[int]:
        if len(value) < 3:
            raise ValueError("area must have at least 3 nodes")
        return value


class RevitAnalyticalSapImportModel(BaseModel):
    # Contract source: export-analytical-model/src/AnalyticalExport/Models/ModelDtos.cs
    model_config = ConfigDict(extra="forbid")

    nodes: List[ExportNodeModel]
    lines: List[ExportLineModel]
    areas: List[ExportAreaModel]

    @model_validator(mode="after")
    def validate_references(self) -> "RevitAnalyticalSapImportModel":
        node_ids = [node.node_id for node in self.nodes]
        line_ids = [line.line_id for line in self.lines]
        area_ids = [area.area_id for area in self.areas]

        if len(node_ids) != len(set(node_ids)):
            raise ValueError("node_id values must be unique")
        if len(line_ids) != len(set(line_ids)):
            raise ValueError("line_id values must be unique")
        if len(area_ids) != len(set(area_ids)):
            raise ValueError("area_id values must be unique")

        node_id_set = set(node_ids)
        for line in self.lines:
            if line.Ni not in node_id_set or line.Nj not in node_id_set:
                raise ValueError(f"line {line.line_id} references a missing node")

        for area in self.areas:
            missing = [node_id for node_id in area.nodes if node_id not in node_id_set]
            if missing:
                raise ValueError(f"area {area.area_id} references missing nodes: {missing}")

        return self

    @classmethod
    def from_path(cls, path: str | Path) -> "RevitAnalyticalSapImportModel":
        source_path = Path(path).resolve()
        return cls.model_validate_json(source_path.read_text(encoding="utf-8"))


class RevitAnalyticalSapGenerator:
    def __init__(
        self,
        payload: RevitAnalyticalSapImportModel | Dict[str, Any] | str | Path,
        *,
        custom_sections: Optional[Dict[str, Dict[str, float]]] = None,
        initialize_blank: bool = True,
        units: int = 6,
        material_name: str = "S355",
        concrete_material_name: str = "C30",
        slab_thickness: float = 0.15,
        apply_supports: bool = True,
        apply_loads: bool = True,
        dead_self_weight_multiplier: float = 0.0,
    ) -> None:
        self.payload = self.coerce_payload(payload)
        self.custom_sections = custom_sections
        self.initialize_blank = initialize_blank
        self.units = units
        self.material_name = material_name
        self.concrete_material_name = concrete_material_name
        self.slab_thickness = slab_thickness
        self.apply_supports = apply_supports
        self.apply_loads = apply_loads
        self.dead_self_weight_multiplier = dead_self_weight_multiplier

    @staticmethod
    def coerce_payload(
        payload: RevitAnalyticalSapImportModel | Dict[str, Any] | str | Path,
    ) -> RevitAnalyticalSapImportModel:
        if isinstance(payload, RevitAnalyticalSapImportModel):
            return payload
        if isinstance(payload, (str, Path)):
            return RevitAnalyticalSapImportModel.from_path(payload)
        return RevitAnalyticalSapImportModel.model_validate(payload)

    def metadata_payload(self, metadata: BaseModel | None) -> Dict[str, Any] | None:
        if metadata is None:
            return None

        dumped = metadata.model_dump(mode="python", exclude_none=True)
        if not dumped:
            return None

        return {"metadata": dumped}

    def build_geometry_model(self) -> Model:
        model = Model()

        for node in self.payload.nodes:
            model.create_node(
                x=node.x,
                y=node.y,
                z=node.z,
                node_id=node.node_id,
                metadata=self.metadata_payload(node.metadata),
            )

        for line in self.payload.lines:
            model.create_line(
                ni=line.Ni,
                nj=line.Nj,
                section=line.section,
                member_type=line.type,
                line_id=line.line_id,
                metadata=self.metadata_payload(line.metadata),
            )

        for area in self.payload.areas:
            model.create_area(
                node_ids=area.nodes,
                section=area.section,
                area_type=area.type,
                area_id=area.area_id,
                metadata=self.metadata_payload(area.metadata),
            )

        return model

    def collect_supports(self) -> Dict[int, List[int]]:
        supports: Dict[int, List[int]] = {}
        for node in self.payload.nodes:
            support = node.metadata.support if node.metadata else None
            if support is None:
                continue
            supports[node.node_id] = support.restraint
        return supports

    def collect_area_loads(self) -> List[Dict[str, Any]]:
        loads: List[Dict[str, Any]] = []
        for area in self.payload.areas:
            if area.metadata is None:
                continue
            for load in area.metadata.loads:
                payload = load.model_dump(mode="python", exclude_none=True)
                payload["area_id"] = area.area_id
                loads.append(payload)
        return loads

    def generate(self) -> Dict[str, Any]:
        from sap2000.core import Sap2000Session, assign_supports_by_node_ids, import_structural_model
        from sap2000.slab_loads import apply_uniform_area_loads_from_revit_export

        geometry_model = self.build_geometry_model()
        supports_by_node_id = self.collect_supports()
        area_loads = self.collect_area_loads()

        with Sap2000Session() as sap:
            sap_result = import_structural_model(
                sap.SapModel,
                geometry_model,
                material_name=self.material_name,
                custom_sections=self.custom_sections,
                concrete_material_name=self.concrete_material_name,
                slab_thickness=self.slab_thickness,
                initialize_blank=self.initialize_blank,
                units=self.units,
            )

            support_result: Optional[Dict[int, Dict[str, Any]]] = None
            if self.apply_supports and supports_by_node_id:
                support_result = assign_supports_by_node_ids(
                    sap.SapModel,
                    point_names=sap_result["points"],
                    restraints_by_node_id=supports_by_node_id,
                )

            loading_result: Optional[Dict[str, Any]] = None
            if self.apply_loads and area_loads and sap_result.get("areas"):
                loading_result = apply_uniform_area_loads_from_revit_export(
                    sap.SapModel,
                    area_name_by_area_id=sap_result["areas"],
                    load_payloads=area_loads,
                    default_self_weight_multiplier=self.dead_self_weight_multiplier,
                )

            return {
                "input_model": self.payload,
                "geometry": geometry_model,
                "sap2000": sap_result,
                "supports": support_result,
                "loading": loading_result,
            }
