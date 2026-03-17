using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnalyticalImport.Models;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using WinForms = System.Windows.Forms;

namespace AnalyticalImport;

[Transaction(TransactionMode.Manual)]
public sealed class ImportAnalyticalModelCommand : IExternalCommand
{
    private const string SteelMaterialName = "Steel";
    private const string ConcreteMaterialName = "Concrete, Cast-in-Place gray";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Dictionary<string, string> SectionNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Backward-compatible aliases from earlier geometry exports.
        ["UB203X133X25"] = "UB254x146x31",
        ["UB254X146X31"] = "UB254x146x31",
        ["UB305X165X40"] = "UB254x146x31",
        ["UB356X171X45"] = "UB254x146x31",
        ["UB203X102X23"] = "UB254x146x31",
        ["UC254X254X73"] = "UC356x406x551",
    };

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uiDocument = commandData.Application.ActiveUIDocument;
        Document document = uiDocument.Document;

        if (document.IsFamilyDocument)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                "Open a Revit project document before running the analytical import.");
            return Result.Failed;
        }

        string? path = PromptForJsonPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Cancelled;
        }

        SettingsStore.SaveLastJsonPath(path);

        ModelDto? model;
        try
        {
            model = ReadJson(path);
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                $"Failed to read JSON:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        if (model is null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                "The JSON file is empty or could not be deserialized.");
            return Result.Failed;
        }

        if (model.Nodes.Count == 0 && model.Lines.Count == 0 && model.Areas.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                "The JSON file does not contain nodes, lines, or areas.");
            return Result.Failed;
        }

        Dictionary<int, XYZ> nodeLookup;
        try
        {
            nodeLookup = model.Nodes.ToDictionary(
                node => node.NodeId,
                node => new XYZ(
                    MetersToFeet(node.X),
                    MetersToFeet(node.Y),
                    MetersToFeet(node.Z)));
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                $"Invalid node data:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        Dictionary<string, FamilySymbol> sectionSymbols = BuildSectionSymbolIndex(document);
        List<string> missingSections = FindMissingSections(model, sectionSymbols);
        if (missingSections.Count > 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                "Missing section types in this Revit project." + Environment.NewLine +
                "Load Structural Framing / Structural Columns types with these names:" + Environment.NewLine + Environment.NewLine +
                "- " + string.Join(Environment.NewLine + "- ", missingSections));
            return Result.Failed;
        }

        int membersCreated = 0;
        int panelsCreated = 0;

        try
        {
            using Transaction transaction = new(document, "Import Analytical Members & Panels");
            transaction.Start();

            Material steel = GetOrCreateMaterial(document, SteelMaterialName);
            Material concrete = GetOrCreateMaterial(document, ConcreteMaterialName);

            foreach (LineDto line in model.Lines)
            {
                if (!nodeLookup.TryGetValue(line.Ni, out XYZ? start) || !nodeLookup.TryGetValue(line.Nj, out XYZ? end))
                {
                    throw new InvalidOperationException($"Line {line.LineId} references missing nodes.");
                }

                Curve curve = Line.CreateBound(start, end);
                AnalyticalMember member = AnalyticalMember.Create(document, curve);

                string sectionKey = MapSectionName(line.Section);
                member.SectionTypeId = sectionSymbols[sectionKey].Id;
                member.MaterialId = steel.Id;
                member.StructuralRole = ResolveMemberRole(line.Type);

                membersCreated++;
            }

            foreach (AreaDto area in model.Areas)
            {
                if (area.Nodes.Count < 3)
                {
                    throw new InvalidOperationException($"Area {area.AreaId} has fewer than 3 nodes.");
                }

                List<XYZ> points = new(area.Nodes.Count);
                foreach (int nodeId in area.Nodes)
                {
                    if (!nodeLookup.TryGetValue(nodeId, out XYZ? point))
                    {
                        throw new InvalidOperationException($"Area {area.AreaId} references missing node {nodeId}.");
                    }

                    points.Add(point);
                }

                if (!TryBuildCurveLoop(points, out CurveLoop? loop, out string? loopError))
                {
                    throw new InvalidOperationException($"Area {area.AreaId} boundary error: {loopError}");
                }

                AnalyticalPanel panel = AnalyticalPanel.Create(document, loop);
                panel.MaterialId = concrete.Id;
                panel.StructuralRole = ResolvePanelRole(area.Type);
                panel.Thickness = MillimetersToFeet(ParseThicknessMmFromSection(area.Section));

                panelsCreated++;
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Analytical Import",
                $"Import failed:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        Autodesk.Revit.UI.TaskDialog.Show(
            "Analytical Import",
            $"Created:{Environment.NewLine}- Analytical Members: {membersCreated}{Environment.NewLine}- Analytical Panels: {panelsCreated}");

        return Result.Succeeded;
    }

    private static string? PromptForJsonPath()
    {
        string? lastPath = SettingsStore.LoadLastJsonPath();

        using WinForms.OpenFileDialog dialog = new();
        dialog.Title = "Select analytical model JSON";
        dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
        dialog.Multiselect = false;

        if (!string.IsNullOrWhiteSpace(lastPath))
        {
            string? lastDirectory = Path.GetDirectoryName(lastPath);
            if (!string.IsNullOrWhiteSpace(lastDirectory) && Directory.Exists(lastDirectory))
            {
                dialog.InitialDirectory = lastDirectory;
            }

            if (File.Exists(lastPath))
            {
                dialog.FileName = Path.GetFileName(lastPath);
            }
        }

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
    }

    private static ModelDto ReadJson(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ModelDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("JSON deserialization returned null.");
    }

    private static List<string> FindMissingSections(ModelDto model, IReadOnlyDictionary<string, FamilySymbol> sectionSymbols)
    {
        return model.Lines
            .Select(line => MapSectionName(line.Section))
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(section => !sectionSymbols.ContainsKey(section))
            .OrderBy(section => section, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, FamilySymbol> BuildSectionSymbolIndex(Document document)
    {
        Dictionary<string, FamilySymbol> symbols = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<Element> framingTypes = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralFraming)
            .WhereElementIsElementType()
            .ToElements();

        IEnumerable<Element> columnTypes = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsElementType()
            .ToElements();

        foreach (Element element in framingTypes.Concat(columnTypes))
        {
            if (element is FamilySymbol familySymbol && !symbols.ContainsKey(familySymbol.Name))
            {
                symbols.Add(familySymbol.Name, familySymbol);
            }
        }

        return symbols;
    }

    private static Material GetOrCreateMaterial(Document document, string name)
    {
        Material? material = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

        if (material is not null)
        {
            return material;
        }

        ElementId materialId = Material.Create(document, name);
        return (Material)document.GetElement(materialId);
    }

    private static AnalyticalStructuralRole ResolveMemberRole(string? lineType)
    {
        return string.Equals(lineType, "column", StringComparison.OrdinalIgnoreCase)
            ? AnalyticalStructuralRole.StructuralRoleColumn
            : AnalyticalStructuralRole.StructuralRoleBeam;
    }

    private static AnalyticalStructuralRole ResolvePanelRole(string? areaType)
    {
        _ = areaType;
        return AnalyticalStructuralRole.StructuralRoleFloor;
    }

    private static string MapSectionName(string? section)
    {
        string normalized = (section ?? string.Empty).Trim();
        return SectionNameMap.TryGetValue(normalized, out string? mapped)
            ? mapped
            : normalized;
    }

    private static double ParseThicknessMmFromSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new InvalidOperationException("Panel section is empty; expected something like SLAB150.");
        }

        Match match = Regex.Match(section, @"(\d+(?:\.\d+)?)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Cannot parse slab thickness from '{section}'. Expected something like SLAB150.");
        }

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static bool TryBuildCurveLoop(IReadOnlyList<XYZ> points, out CurveLoop? loop, out string? error)
    {
        const double MinEdgeLengthFeet = 1e-6;

        loop = null;
        error = null;

        try
        {
            CurveLoop candidate = new();

            for (int index = 0; index < points.Count; index++)
            {
                XYZ start = points[index];
                XYZ end = points[(index + 1) % points.Count];

                if (start.DistanceTo(end) < MinEdgeLengthFeet)
                {
                    error = "Zero-length edge detected.";
                    return false;
                }

                candidate.Append(Line.CreateBound(start, end));
            }

            loop = candidate;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static double MetersToFeet(double meters)
    {
        return UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    }

    private static double MillimetersToFeet(double millimeters)
    {
        return UnitUtils.ConvertToInternalUnits(millimeters / 1000.0, UnitTypeId.Meters);
    }
}
