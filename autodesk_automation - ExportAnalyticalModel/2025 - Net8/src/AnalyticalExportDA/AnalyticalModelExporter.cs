using System.Globalization;
using AnalyticalExportDA.Models;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace AnalyticalExportDA;

internal static class AnalyticalModelExporter
{
    public static AnalyticalExportResult Export(Document document)
    {
        ModelDto model = new();
        List<string> warnings = new();
        Dictionary<string, NodeDto> nodeByCoordKey = new(StringComparer.Ordinal);
        Dictionary<int, AreaDto> areaByPanelId = new();
        AnalyticalToPhysicalAssociationManager? associationManager =
            AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(document);

        List<AnalyticalMember> members = new FilteredElementCollector(document)
            .OfClass(typeof(AnalyticalMember))
            .Cast<AnalyticalMember>()
            .OrderBy(item => item.Id.IntegerValue)
            .ToList();

        foreach (AnalyticalMember member in members)
        {
            Curve curve;
            try
            {
                curve = member.GetCurve();
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped member {member.Id.IntegerValue}: {ex.Message}");
                continue;
            }

            if (curve is not Line lineCurve)
            {
                warnings.Add($"Skipped member {member.Id.IntegerValue}: only straight analytical members are exported.");
                continue;
            }

            int startNodeId = GetOrCreateNodeId(
                model,
                nodeByCoordKey,
                lineCurve.GetEndPoint(0),
                "member",
                member);
            int endNodeId = GetOrCreateNodeId(
                model,
                nodeByCoordKey,
                lineCurve.GetEndPoint(1),
                "member",
                member);

            FamilySymbol? sectionType = document.GetElement(member.SectionTypeId) as FamilySymbol;

            model.Lines.Add(new LineDto
            {
                LineId = model.Lines.Count + 1,
                Ni = startNodeId,
                Nj = endNodeId,
                Section = sectionType?.Name ?? string.Empty,
                Type = MapMemberType(member.StructuralRole),
                Metadata = BuildMemberMetadata(document, associationManager, member, sectionType),
            });
        }

        List<AnalyticalPanel> panels = new FilteredElementCollector(document)
            .OfClass(typeof(AnalyticalPanel))
            .Cast<AnalyticalPanel>()
            .OrderBy(item => item.Id.IntegerValue)
            .ToList();

        foreach (AnalyticalPanel panel in panels)
        {
            CurveLoop outerContour;
            try
            {
                outerContour = panel.GetOuterContour();
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped panel {panel.Id.IntegerValue}: {ex.Message}");
                continue;
            }

            if (!TryExtractLoopNodeIds(model, nodeByCoordKey, outerContour, "panel", panel, out List<int>? nodeIds, out string? error))
            {
                warnings.Add($"Skipped panel {panel.Id.IntegerValue}: {error}");
                continue;
            }

            Material? material = document.GetElement(panel.MaterialId) as Material;
            AreaDto area = new()
            {
                AreaId = model.Areas.Count + 1,
                Nodes = nodeIds,
                Section = BuildPanelSectionName(panel, material),
                Type = MapPanelType(panel.StructuralRole),
                Metadata = BuildPanelMetadata(document, associationManager, model, nodeByCoordKey, panel, material, warnings),
            };

            model.Areas.Add(area);
            areaByPanelId[panel.Id.IntegerValue] = area;
        }

        List<AreaLoad> areaLoads = new FilteredElementCollector(document)
            .OfClass(typeof(AreaLoad))
            .Cast<AreaLoad>()
            .OrderBy(item => item.Id.IntegerValue)
            .ToList();

        foreach (AreaLoad load in areaLoads)
        {
            if (!load.IsHosted || !load.IsConstrainedOnHost)
            {
                continue;
            }

            if (load.NumRefPoints != 1)
            {
                warnings.Add($"Skipped area load {load.Id.IntegerValue}: only uniform hosted area loads are exported.");
                continue;
            }

            if (!areaByPanelId.TryGetValue(load.HostElementId.IntegerValue, out AreaDto? hostArea))
            {
                continue;
            }

            Dictionary<string, object?> hostMetadata = hostArea.Metadata ??= new Dictionary<string, object?>();
            List<object?> loads = GetOrCreateObjectList(hostMetadata, "loads");
            loads.Add(BuildAreaLoadMetadata(document, load));
        }

        return new AnalyticalExportResult(model, warnings);
    }

    private static Dictionary<string, object?> BuildMemberMetadata(
        Document document,
        AnalyticalToPhysicalAssociationManager? associationManager,
        AnalyticalMember member,
        FamilySymbol? sectionType)
    {
        Dictionary<string, object?> metadata = CreateElementIdentity(member);
        metadata["structural_role"] = member.StructuralRole.ToString();
        metadata["material_name"] = GetMaterialName(document, member.MaterialId);
        metadata["section_shape"] = GetSectionShape(sectionType);
        metadata["cross_section_rotation_deg"] = Math.Round(RadiansToDegrees(member.CrossSectionRotation), 6);

        AppendAssociatedPhysicalElement(metadata, document, associationManager, member.Id);
        return metadata;
    }

    private static Dictionary<string, object?> BuildPanelMetadata(
        Document document,
        AnalyticalToPhysicalAssociationManager? associationManager,
        ModelDto model,
        Dictionary<string, NodeDto> nodeByCoordKey,
        AnalyticalPanel panel,
        Material? material,
        List<string> warnings)
    {
        Dictionary<string, object?> metadata = CreateElementIdentity(panel);
        metadata["structural_role"] = panel.StructuralRole.ToString();
        metadata["material_name"] = material?.Name ?? string.Empty;
        metadata["thickness_mm"] = Math.Round(FeetToMillimeters(panel.Thickness), 3);
        metadata["openings"] = BuildOpeningMetadata(document, model, nodeByCoordKey, panel, warnings);
        metadata["loads"] = new List<object?>();

        AppendAssociatedPhysicalElement(metadata, document, associationManager, panel.Id);
        return metadata;
    }

    private static List<object?> BuildOpeningMetadata(
        Document document,
        ModelDto model,
        Dictionary<string, NodeDto> nodeByCoordKey,
        AnalyticalPanel panel,
        List<string> warnings)
    {
        List<object?> openings = new();

        ICollection<ElementId> openingIds;
        try
        {
            openingIds = panel.GetAnalyticalOpeningsIds();
        }
        catch (Exception ex)
        {
            warnings.Add($"Skipped openings for panel {panel.Id.IntegerValue}: {ex.Message}");
            return openings;
        }

        foreach (ElementId openingId in openingIds)
        {
            if (document.GetElement(openingId) is not AnalyticalOpening opening)
            {
                continue;
            }

            CurveLoop openingContour;
            try
            {
                openingContour = opening.GetOuterContour();
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped opening {opening.Id.IntegerValue}: {ex.Message}");
                continue;
            }

            if (!TryExtractLoopNodeIds(model, nodeByCoordKey, openingContour, "opening", opening, out List<int>? nodeIds, out string? error))
            {
                warnings.Add($"Skipped opening {opening.Id.IntegerValue}: {error}");
                continue;
            }

            Dictionary<string, object?> payload = CreateElementIdentity(opening);
            payload["panel_element_id"] = panel.Id.IntegerValue;
            payload["panel_unique_id"] = panel.UniqueId;
            payload["nodes"] = nodeIds;
            openings.Add(payload);
        }

        return openings;
    }

    private static Dictionary<string, object?> BuildAreaLoadMetadata(Document document, AreaLoad load)
    {
        Dictionary<string, object?> metadata = CreateElementIdentity(load);
        metadata["kind"] = "area_uniform";
        metadata["load_case_name"] = load.LoadCaseName;
        metadata["load_category_name"] = load.LoadCategoryName;
        metadata["load_nature_name"] = load.LoadNatureName;
        metadata["is_hosted"] = load.IsHosted;
        metadata["is_constrained_on_host"] = load.IsConstrainedOnHost;
        metadata["is_projected"] = load.IsProjected;
        metadata["orient_to"] = load.OrientTo.ToString();
        metadata["force_vector_global_kn_per_m2"] = ConvertAreaForceVectorToGlobal(load.ForceVector1);
        metadata["num_reference_points"] = load.NumRefPoints;
        metadata["host_element_id"] = load.HostElementId.IntegerValue;
        metadata["host_unique_id"] = document.GetElement(load.HostElementId)?.UniqueId ?? string.Empty;
        return metadata;
    }

    private static string BuildPanelSectionName(AnalyticalPanel panel, Material? material)
    {
        string materialName = string.IsNullOrWhiteSpace(material?.Name) ? "Panel" : material.Name.Trim();
        double thicknessMm = Math.Round(FeetToMillimeters(panel.Thickness), 3);
        string thicknessLabel = thicknessMm.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{materialName} {thicknessLabel}mm";
    }

    private static bool TryExtractLoopNodeIds(
        ModelDto model,
        Dictionary<string, NodeDto> nodeByCoordKey,
        CurveLoop loop,
        string sourceKind,
        Element sourceElement,
        out List<int>? nodeIds,
        out string? error)
    {
        nodeIds = null;
        error = null;

        if (!TryExtractLoopVertices(loop, out List<XYZ>? vertices, out error))
        {
            return false;
        }

        nodeIds = new List<int>(vertices.Count);
        foreach (XYZ point in vertices)
        {
            nodeIds.Add(GetOrCreateNodeId(model, nodeByCoordKey, point, sourceKind, sourceElement));
        }

        return true;
    }

    private static bool TryExtractLoopVertices(CurveLoop loop, out List<XYZ>? vertices, out string? error)
    {
        vertices = null;
        error = null;

        List<Curve> curves = loop.Cast<Curve>().ToList();
        if (curves.Count < 3)
        {
            error = "Contour must contain at least three edges.";
            return false;
        }

        List<XYZ> orderedVertices = new(curves.Count);
        XYZ? currentEnd = null;

        for (int index = 0; index < curves.Count; index++)
        {
            if (curves[index] is not Line line)
            {
                error = "Contour contains a non-linear edge.";
                return false;
            }

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);

            if (index == 0)
            {
                orderedVertices.Add(start);
                currentEnd = end;
                continue;
            }

            if (currentEnd!.IsAlmostEqualTo(start))
            {
                orderedVertices.Add(start);
                currentEnd = end;
                continue;
            }

            if (currentEnd.IsAlmostEqualTo(end))
            {
                orderedVertices.Add(end);
                currentEnd = start;
                continue;
            }

            error = "Contour edges are not connected.";
            return false;
        }

        if (!currentEnd!.IsAlmostEqualTo(orderedVertices[0]))
        {
            error = "Contour is not closed.";
            return false;
        }

        vertices = orderedVertices;
        return true;
    }

    private static int GetOrCreateNodeId(
        ModelDto model,
        Dictionary<string, NodeDto> nodeByCoordKey,
        XYZ point,
        string sourceKind,
        Element sourceElement)
    {
        string coordKey = BuildCoordKey(point);
        if (nodeByCoordKey.TryGetValue(coordKey, out NodeDto? existing))
        {
            AppendNodeSourceRef(existing, sourceKind, sourceElement);
            return existing.NodeId;
        }

        Dictionary<string, object?> metadata = new()
        {
            ["coord_key"] = coordKey,
            ["source_refs"] = new List<object?>(),
        };

        NodeDto node = new()
        {
            NodeId = model.Nodes.Count + 1,
            X = Math.Round(FeetToMeters(point.X), 6),
            Y = Math.Round(FeetToMeters(point.Y), 6),
            Z = Math.Round(FeetToMeters(point.Z), 6),
            Metadata = metadata,
        };

        AppendNodeSourceRef(node, sourceKind, sourceElement);
        model.Nodes.Add(node);
        nodeByCoordKey[coordKey] = node;
        return node.NodeId;
    }

    private static void AppendNodeSourceRef(NodeDto node, string sourceKind, Element sourceElement)
    {
        Dictionary<string, object?> metadata = node.Metadata ??= new Dictionary<string, object?>();
        List<object?> sourceRefs = GetOrCreateObjectList(metadata, "source_refs");

        bool alreadyPresent = sourceRefs
            .OfType<Dictionary<string, object?>>()
            .Any(item =>
                string.Equals(item.GetValueOrDefault("kind")?.ToString(), sourceKind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.GetValueOrDefault("revit_element_id")?.ToString(), sourceElement.Id.IntegerValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

        if (alreadyPresent)
        {
            return;
        }

        sourceRefs.Add(new Dictionary<string, object?>
        {
            ["kind"] = sourceKind,
            ["revit_element_id"] = sourceElement.Id.IntegerValue,
            ["revit_unique_id"] = sourceElement.UniqueId,
        });
    }

    private static List<object?> GetOrCreateObjectList(Dictionary<string, object?> metadata, string key)
    {
        if (metadata.TryGetValue(key, out object? existing) && existing is List<object?> list)
        {
            return list;
        }

        List<object?> created = new();
        metadata[key] = created;
        return created;
    }

    private static Dictionary<string, object?> CreateElementIdentity(Element element)
    {
        return new Dictionary<string, object?>
        {
            ["revit_element_id"] = element.Id.IntegerValue,
            ["revit_unique_id"] = element.UniqueId,
        };
    }

    private static void AppendAssociatedPhysicalElement(
        Dictionary<string, object?> metadata,
        Document document,
        AnalyticalToPhysicalAssociationManager? associationManager,
        ElementId analyticalElementId)
    {
        if (associationManager is null)
        {
            return;
        }

        ElementId associatedId = associationManager.GetAssociatedElementId(analyticalElementId);
        if (associatedId == ElementId.InvalidElementId)
        {
            return;
        }

        Element? physicalElement = document.GetElement(associatedId);
        if (physicalElement is null)
        {
            return;
        }

        metadata["physical_element_id"] = physicalElement.Id.IntegerValue;
        metadata["physical_unique_id"] = physicalElement.UniqueId;
    }

    private static string GetMaterialName(Document document, ElementId materialId)
    {
        if (materialId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        return (document.GetElement(materialId) as Material)?.Name ?? string.Empty;
    }

    private static string GetSectionShape(FamilySymbol? sectionType)
    {
        if (sectionType is null)
        {
            return string.Empty;
        }

        Parameter? shape = sectionType.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_SHAPE);
        return shape?.AsValueString() ?? shape?.AsString() ?? sectionType.FamilyName ?? string.Empty;
    }

    private static string MapMemberType(AnalyticalStructuralRole role)
    {
        return role switch
        {
            AnalyticalStructuralRole.StructuralRoleColumn => "column",
            AnalyticalStructuralRole.StructuralRoleBeam => "beam",
            _ => NormalizeRoleName(role),
        };
    }

    private static string MapPanelType(AnalyticalStructuralRole role)
    {
        return role switch
        {
            AnalyticalStructuralRole.StructuralRoleFloor => "slab",
            _ => NormalizeRoleName(role),
        };
    }

    private static string NormalizeRoleName(AnalyticalStructuralRole role)
    {
        string name = role.ToString().Replace("StructuralRole", string.Empty, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(name)
            ? "unknown"
            : name.Trim().ToLowerInvariant();
    }

    private static Dictionary<string, double> ConvertAreaForceVectorToGlobal(XYZ vector)
    {
        return new Dictionary<string, double>
        {
            ["x"] = Math.Round(UnitUtils.ConvertFromInternalUnits(vector.X, UnitTypeId.KilonewtonsPerSquareMeter), 6),
            ["y"] = Math.Round(UnitUtils.ConvertFromInternalUnits(vector.Y, UnitTypeId.KilonewtonsPerSquareMeter), 6),
            ["z"] = Math.Round(UnitUtils.ConvertFromInternalUnits(vector.Z, UnitTypeId.KilonewtonsPerSquareMeter), 6),
        };
    }

    private static string BuildCoordKey(XYZ point)
    {
        return string.Join(
            ",",
            Math.Round(FeetToMeters(point.X), 6).ToString("0.000000", CultureInfo.InvariantCulture),
            Math.Round(FeetToMeters(point.Y), 6).ToString("0.000000", CultureInfo.InvariantCulture),
            Math.Round(FeetToMeters(point.Z), 6).ToString("0.000000", CultureInfo.InvariantCulture));
    }

    private static double FeetToMeters(double feet)
    {
        return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);
    }

    private static double FeetToMillimeters(double feet)
    {
        return FeetToMeters(feet) * 1000.0;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }
}

internal sealed record AnalyticalExportResult(ModelDto Model, List<string> Warnings);
