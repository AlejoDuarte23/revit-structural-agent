using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RevitModelImport.Models;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using WinForms = System.Windows.Forms;

namespace RevitModelImport;

[Transaction(TransactionMode.Manual)]
public sealed class CreateRevitModelCommand : IExternalCommand
{
    private const string ConcreteMaterialName = "Concrete, Cast-in-Place gray";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Dictionary<string, string> SectionNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
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
                "Create Revit Model",
                "Open a Revit project document before creating the structural model.");
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
                "Create Revit Model",
                $"Failed to read JSON:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        if (model is null)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Create Revit Model",
                "The JSON file is empty or could not be deserialized.");
            return Result.Failed;
        }

        if (model.Nodes.Count == 0 && model.Lines.Count == 0 && model.Areas.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Create Revit Model",
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
                "Create Revit Model",
                $"Invalid node data:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        Dictionary<string, FamilySymbol> framingSymbols = BuildSymbolIndex(document, BuiltInCategory.OST_StructuralFraming);
        Dictionary<string, FamilySymbol> columnSymbols = BuildSymbolIndex(document, BuiltInCategory.OST_StructuralColumns);
        List<string> missingSections = FindMissingMemberSections(model, framingSymbols, columnSymbols);
        if (missingSections.Count > 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Create Revit Model",
                "Missing steel section types in this Revit project." + Environment.NewLine +
                "Load Structural Framing / Structural Columns types with these names:" + Environment.NewLine + Environment.NewLine +
                "- " + string.Join(Environment.NewLine + "- ", missingSections));
            return Result.Failed;
        }

        BuildSummary summary;

        try
        {
            using Transaction transaction = new(document, "Create Revit Structural Model");
            transaction.Start();

            Material concrete = GetOrCreateMaterial(document, ConcreteMaterialName);
            PhysicalModelBuilder builder = new(document, nodeLookup, framingSymbols, columnSymbols, concrete.Id);
            summary = builder.Create(model);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Create Revit Model",
                $"Model creation failed:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        Autodesk.Revit.UI.TaskDialog.Show(
            "Create Revit Model",
            "Created:" + Environment.NewLine +
            $"- Framing: {summary.BeamsCreated}{Environment.NewLine}" +
            $"- Columns: {summary.ColumnsCreated}{Environment.NewLine}" +
            $"- Slabs: {summary.SlabsCreated}{Environment.NewLine}" +
            $"- Levels: {summary.LevelsCreated}{Environment.NewLine}" +
            $"- Floor Types: {summary.FloorTypesCreated}{Environment.NewLine}{Environment.NewLine}" +
            "Skipped:" + Environment.NewLine +
            $"- Members: {summary.MembersSkipped}{Environment.NewLine}" +
            $"- Slabs: {summary.SlabsSkipped}{Environment.NewLine}{Environment.NewLine}" +
            $"Errors: {summary.Errors}" +
            (summary.ErrorMessages.Count == 0
                ? string.Empty
                : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, summary.ErrorMessages)));

        return Result.Succeeded;
    }

    private static string? PromptForJsonPath()
    {
        string? lastPath = SettingsStore.LoadLastJsonPath();

        using WinForms.OpenFileDialog dialog = new();
        dialog.Title = "Select structural model JSON";
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

    private static List<string> FindMissingMemberSections(
        ModelDto model,
        IReadOnlyDictionary<string, FamilySymbol> framingSymbols,
        IReadOnlyDictionary<string, FamilySymbol> columnSymbols)
    {
        HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);

        foreach (LineDto line in model.Lines)
        {
            string section = MapSectionName(line.Section);
            if (string.IsNullOrWhiteSpace(section))
            {
                continue;
            }

            bool isColumn = string.Equals(line.Type, "column", StringComparison.OrdinalIgnoreCase);
            bool exists = isColumn
                ? columnSymbols.ContainsKey(section)
                : framingSymbols.ContainsKey(section);

            if (!exists)
            {
                missing.Add(section);
            }
        }

        return missing
            .OrderBy(section => section, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, FamilySymbol> BuildSymbolIndex(Document document, BuiltInCategory category)
    {
        Dictionary<string, FamilySymbol> symbols = new(StringComparer.OrdinalIgnoreCase);

        IEnumerable<Element> types = new FilteredElementCollector(document)
            .OfCategory(category)
            .WhereElementIsElementType()
            .ToElements();

        foreach (Element element in types)
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
            throw new InvalidOperationException("Slab section is empty; expected something like Concrete 150mm.");
        }

        Match match = Regex.Match(section, @"(\d+(?:\.\d+)?)");
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Cannot parse slab thickness from '{section}'. Expected something like Concrete 150mm.");
        }

        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static double MetersToFeet(double meters)
    {
        return UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
    }

    private static double MillimetersToFeet(double millimeters)
    {
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    }

    private sealed class PhysicalModelBuilder
    {
        private static readonly double GeometryToleranceFeet =
            UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

        private readonly Document _document;
        private readonly IReadOnlyDictionary<int, XYZ> _nodeLookup;
        private readonly IReadOnlyDictionary<string, FamilySymbol> _framingSymbols;
        private readonly IReadOnlyDictionary<string, FamilySymbol> _columnSymbols;
        private readonly ElementId _concreteMaterialId;
        private readonly Dictionary<string, Level> _levelsByElevation = new(StringComparer.Ordinal);
        private readonly HashSet<string> _levelNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FloorType> _floorTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _existingMemberKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _existingFloorKeys = new(StringComparer.Ordinal);

        public PhysicalModelBuilder(
            Document document,
            IReadOnlyDictionary<int, XYZ> nodeLookup,
            IReadOnlyDictionary<string, FamilySymbol> framingSymbols,
            IReadOnlyDictionary<string, FamilySymbol> columnSymbols,
            ElementId concreteMaterialId)
        {
            _document = document;
            _nodeLookup = nodeLookup;
            _framingSymbols = framingSymbols;
            _columnSymbols = columnSymbols;
            _concreteMaterialId = concreteMaterialId;

            foreach (Level level in new FilteredElementCollector(document)
                         .OfClass(typeof(Level))
                         .Cast<Level>())
            {
                _levelsByElevation[ElevationKey(level.Elevation)] = level;
                if (!string.IsNullOrWhiteSpace(level.Name))
                {
                    _levelNames.Add(level.Name);
                }
            }

            foreach (FloorType floorType in new FilteredElementCollector(document)
                         .OfClass(typeof(FloorType))
                         .Cast<FloorType>())
            {
                if (!_floorTypes.ContainsKey(floorType.Name))
                {
                    _floorTypes.Add(floorType.Name, floorType);
                }
            }

            foreach (string key in CollectExistingMemberKeys(document))
            {
                _existingMemberKeys.Add(key);
            }

            foreach (string key in CollectExistingFloorKeys(document))
            {
                _existingFloorKeys.Add(key);
            }
        }

        public BuildSummary Create(ModelDto model)
        {
            BuildSummary summary = new();
            HashSet<string> seenInputMemberKeys = new(StringComparer.Ordinal);
            HashSet<string> seenInputFloorKeys = new(StringComparer.Ordinal);

            foreach (LineDto line in model.Lines)
            {
                if (!_nodeLookup.TryGetValue(line.Ni, out XYZ? start) || start is null ||
                    !_nodeLookup.TryGetValue(line.Nj, out XYZ? end) || end is null)
                {
                    summary.AddError($"Line {line.LineId}: references missing nodes.");
                    continue;
                }

                if (start.DistanceTo(end) < GeometryToleranceFeet)
                {
                    summary.MembersSkipped++;
                    continue;
                }

                bool isColumn = IsColumn(line, start, end);
                string sectionName = MapSectionName(line.Section);

                IReadOnlyDictionary<string, FamilySymbol> symbolIndex = isColumn ? _columnSymbols : _framingSymbols;
                if (!symbolIndex.TryGetValue(sectionName, out FamilySymbol? symbol))
                {
                    throw new InvalidOperationException($"Missing family symbol '{sectionName}'.");
                }

                string inputKey = SegmentKey(ElementId.InvalidElementId, start, end);
                if (!seenInputMemberKeys.Add(inputKey))
                {
                    summary.MembersSkipped++;
                    continue;
                }

                try
                {
                    Activate(symbol);

                    if (isColumn)
                    {
                        XYZ basePoint = start.Z <= end.Z ? start : end;
                        XYZ topPoint = start.Z <= end.Z ? end : start;
                        string modelKey = SegmentKey(symbol.Id, basePoint, topPoint);
                        if (_existingMemberKeys.Contains(modelKey))
                        {
                            summary.MembersSkipped++;
                            continue;
                        }

                        Level baseLevel = GetOrCreateLevel(basePoint.Z, summary);
                        Level topLevel = GetOrCreateLevel(topPoint.Z, summary);

                        FamilyInstance column = CreateColumn(symbol, baseLevel, topLevel, basePoint, topPoint);
                        ZeroExtensionsIfPresent(column);

                        _existingMemberKeys.Add(modelKey);
                        summary.ColumnsCreated++;
                    }
                    else
                    {
                        Level referenceLevel = GetOrCreateLevel(Math.Min(start.Z, end.Z), summary);
                        string modelKey = SegmentKey(symbol.Id, start, end);
                        if (_existingMemberKeys.Contains(modelKey))
                        {
                            summary.MembersSkipped++;
                            continue;
                        }

                        Curve curve = Line.CreateBound(start, end);
                        FamilyInstance framing = _document.Create.NewFamilyInstance(
                            curve,
                            symbol,
                            referenceLevel,
                            StructuralType.Beam);

                        SetDouble(
                            framing,
                            BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION,
                            start.Z - referenceLevel.Elevation);
                        SetDouble(
                            framing,
                            BuiltInParameter.STRUCTURAL_BEAM_END1_ELEVATION,
                            end.Z - referenceLevel.Elevation);

                        TryDisallowJoin(framing, 0);
                        TryDisallowJoin(framing, 1);
                        ZeroExtensionsIfPresent(framing);

                        _existingMemberKeys.Add(modelKey);
                        summary.BeamsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    summary.AddError($"Line {line.LineId}: {ex.Message}");
                }
            }

            foreach (AreaDto area in model.Areas)
            {
                if (area.Nodes.Count < 3)
                {
                    summary.AddError($"Area {area.AreaId}: fewer than 3 boundary nodes.");
                    continue;
                }

                List<XYZ> points = new(area.Nodes.Count);
                bool missingNode = false;
                foreach (int nodeId in area.Nodes)
                {
                    if (!_nodeLookup.TryGetValue(nodeId, out XYZ? point) || point is null)
                    {
                        missingNode = true;
                        break;
                    }

                    points.Add(point);
                }

                if (missingNode)
                {
                    summary.AddError($"Area {area.AreaId}: references missing nodes.");
                    continue;
                }

                try
                {
                    if (!TryGetHorizontalElevation(points, out double elevation, out string? elevationError))
                    {
                        throw new InvalidOperationException(
                            $"Area {area.AreaId} is not a horizontal slab boundary: {elevationError}");
                    }

                    if (!TryBuildCurveLoop(points, out CurveLoop? loop, out string? loopError) || loop is null)
                    {
                        throw new InvalidOperationException($"Area {area.AreaId} boundary error: {loopError}");
                    }

                    List<CurveLoop> profile = new() { loop };
                    if (!BoundaryValidation.IsValidHorizontalBoundary(profile))
                    {
                        throw new InvalidOperationException(
                            $"Area {area.AreaId} boundary is not valid for Floor.Create.");
                    }

                    Level level = GetOrCreateLevel(elevation, summary);
                    FloorType floorType = GetOrCreateFloorType(MapSectionName(area.Section), summary);
                    double heightAboveLevel = elevation - level.Elevation;

                    string inputKey = FloorPolygonKey(area.Section, points);
                    if (!seenInputFloorKeys.Add(inputKey))
                    {
                        summary.SlabsSkipped++;
                        continue;
                    }

                    string modelKey = FloorBoundsKey(floorType.Id, level.Id, heightAboveLevel, points);
                    if (_existingFloorKeys.Contains(modelKey))
                    {
                        summary.SlabsSkipped++;
                        continue;
                    }

                    Floor floor = Floor.Create(
                        _document,
                        profile,
                        floorType.Id,
                        level.Id,
                        true,
                        null,
                        0.0);

                    SetDouble(floor, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, heightAboveLevel);

                    _existingFloorKeys.Add(modelKey);
                    _existingFloorKeys.Add(ExistingFloorKey(floor));
                    summary.SlabsCreated++;
                }
                catch (Exception ex)
                {
                    summary.AddError($"Area {area.AreaId}: {ex.Message}");
                }
            }

            _document.Regenerate();

            return summary;
        }

        private Level GetOrCreateLevel(double elevationFeet, BuildSummary summary)
        {
            string key = ElevationKey(elevationFeet);
            if (_levelsByElevation.TryGetValue(key, out Level? existing))
            {
                return existing;
            }

            Level level = Level.Create(_document, elevationFeet);
            level.Name = NextLevelName(elevationFeet);

            _levelsByElevation[key] = level;
            _levelNames.Add(level.Name);
            summary.LevelsCreated++;
            return level;
        }

        private FloorType GetOrCreateFloorType(string sectionName, BuildSummary summary)
        {
            if (_floorTypes.TryGetValue(sectionName, out FloorType? existing))
            {
                return existing;
            }

            ElementId defaultFloorTypeId = Floor.GetDefaultFloorType(_document, false);
            FloorType? defaultFloorType = _document.GetElement(defaultFloorTypeId) as FloorType;
            if (defaultFloorType is null)
            {
                throw new InvalidOperationException(
                    $"No default floor type exists in this Revit project to create '{sectionName}'.");
            }

            FloorType duplicatedFloorType = (FloorType)defaultFloorType.Duplicate(sectionName);
            double thicknessFeet = MillimetersToFeet(ParseThicknessMmFromSection(sectionName));
            CompoundStructure structure = CompoundStructure.CreateSingleLayerCompoundStructure(
                MaterialFunctionAssignment.Structure,
                thicknessFeet,
                _concreteMaterialId);
            duplicatedFloorType.SetCompoundStructure(structure);

            _floorTypes.Add(sectionName, duplicatedFloorType);
            summary.FloorTypesCreated++;
            return duplicatedFloorType;
        }

        private FamilyInstance CreateColumn(
            FamilySymbol symbol,
            Level baseLevel,
            Level topLevel,
            XYZ basePoint,
            XYZ topPoint)
        {
            XYZ insertPoint = new(basePoint.X, basePoint.Y, baseLevel.Elevation);
            FamilyInstance column = _document.Create.NewFamilyInstance(
                insertPoint,
                symbol,
                baseLevel,
                StructuralType.Column);

            double baseOffset = basePoint.Z - baseLevel.Elevation;
            double topOffset = topPoint.Z - topLevel.Elevation;
            double height = topPoint.Z - basePoint.Z;

            SetDouble(column, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, baseOffset);

            bool usedTopLevel =
                SetElementId(column, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, topLevel.Id) &&
                SetDouble(column, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, topOffset);

            if (!usedTopLevel)
            {
                SetDouble(column, BuiltInParameter.INSTANCE_LENGTH_PARAM, height);
                SetDouble(column, BuiltInParameter.FAMILY_HEIGHT_PARAM, height);
            }

            return column;
        }

        private string NextLevelName(double elevationFeet)
        {
            string metersText = UnitUtils
                .ConvertFromInternalUnits(elevationFeet, UnitTypeId.Meters)
                .ToString("0.###", CultureInfo.InvariantCulture);

            string baseName = $"JSON Level {metersText} m";
            if (_levelNames.Add(baseName))
            {
                return baseName;
            }

            int suffix = 2;
            while (true)
            {
                string candidate = $"{baseName} ({suffix})";
                if (_levelNames.Add(candidate))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private static bool IsColumn(LineDto line, XYZ start, XYZ end)
        {
            if (string.Equals(line.Type, "column", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            double dx = start.X - end.X;
            double dy = start.Y - end.Y;
            double dz = Math.Abs(start.Z - end.Z);
            double xy = Math.Sqrt((dx * dx) + (dy * dy));

            if (dz < GeometryToleranceFeet)
            {
                return false;
            }

            return xy <= GeometryToleranceFeet;
        }

        private static bool TryGetHorizontalElevation(
            IReadOnlyList<XYZ> points,
            out double elevationFeet,
            out string? error)
        {
            elevationFeet = 0.0;
            error = null;

            if (points.Count < 3)
            {
                error = "At least 3 points are required.";
                return false;
            }

            double min = points.Min(point => point.Z);
            double max = points.Max(point => point.Z);
            if ((max - min) > GeometryToleranceFeet)
            {
                error = "All slab boundary points must lie on the same horizontal plane.";
                return false;
            }

            elevationFeet = (min + max) * 0.5;
            return true;
        }

        private static bool TryBuildCurveLoop(
            IReadOnlyList<XYZ> points,
            out CurveLoop? loop,
            out string? error)
        {
            loop = null;
            error = null;

            try
            {
                CurveLoop candidate = new();

                for (int index = 0; index < points.Count; index++)
                {
                    XYZ start = points[index];
                    XYZ end = points[(index + 1) % points.Count];

                    if (start.DistanceTo(end) < GeometryToleranceFeet)
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

        private void Activate(FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
                _document.Regenerate();
            }
        }

        private static bool SetDouble(Element element, BuiltInParameter parameterId, double value)
        {
            try
            {
                Parameter? parameter = element.get_Parameter(parameterId);
                if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                {
                    return false;
                }

                return parameter.Set(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool SetElementId(Element element, BuiltInParameter parameterId, ElementId value)
        {
            try
            {
                Parameter? parameter = element.get_Parameter(parameterId);
                if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId)
                {
                    return false;
                }

                return parameter.Set(value);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDisallowJoin(FamilyInstance framing, int end)
        {
            try
            {
                StructuralFramingUtils.DisallowJoinAtEnd(framing, end);
            }
            catch
            {
            }
        }

        private static void ZeroExtensionsIfPresent(FamilyInstance instance)
        {
            foreach (Parameter parameter in instance.Parameters)
            {
                Definition? definition = parameter.Definition;
                if (definition is null || parameter.IsReadOnly)
                {
                    continue;
                }

                string name = definition.Name ?? string.Empty;
                string normalized = name.ToLowerInvariant();
                if (!normalized.Contains("extension"))
                {
                    continue;
                }

                if (normalized.Contains("start") || normalized.Contains("begin") || normalized.Contains("inicio"))
                {
                    TrySetZero(parameter);
                }

                if (normalized.Contains("end") || normalized.Contains("final"))
                {
                    TrySetZero(parameter);
                }
            }
        }

        private static void TrySetZero(Parameter parameter)
        {
            try
            {
                parameter.Set(0.0);
            }
            catch
            {
            }
        }

        private static IEnumerable<string> CollectExistingMemberKeys(Document document)
        {
            foreach (FamilyInstance framing in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                LocationCurve? locationCurve = framing.Location as LocationCurve;
                Line? line = locationCurve?.Curve as Line;
                if (line is null)
                {
                    continue;
                }

                yield return SegmentKey(framing.Symbol.Id, line.GetEndPoint(0), line.GetEndPoint(1));
            }

            foreach (FamilyInstance column in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                XYZ start;
                XYZ end;

                LocationCurve? locationCurve = column.Location as LocationCurve;
                Line? line = locationCurve?.Curve as Line;
                if (line is not null)
                {
                    start = line.GetEndPoint(0);
                    end = line.GetEndPoint(1);
                }
                else
                {
                    LocationPoint? locationPoint = column.Location as LocationPoint;
                    if (locationPoint is null)
                    {
                        continue;
                    }

                    start = locationPoint.Point;
                    double height = 0.0;

                    Parameter? lengthParameter = column.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM);
                    if (lengthParameter is not null && lengthParameter.StorageType == StorageType.Double)
                    {
                        height = lengthParameter.AsDouble();
                    }

                    if (height <= GeometryToleranceFeet)
                    {
                        BoundingBoxXYZ? boundingBox = column.get_BoundingBox(null);
                        if (boundingBox is not null)
                        {
                            height = Math.Max(0.0, boundingBox.Max.Z - boundingBox.Min.Z);
                        }
                    }

                    end = new XYZ(start.X, start.Y, start.Z + height);
                }

                yield return SegmentKey(column.Symbol.Id, start, end);
            }
        }

        private static IEnumerable<string> CollectExistingFloorKeys(Document document)
        {
            foreach (Floor floor in new FilteredElementCollector(document)
                         .OfClass(typeof(Floor))
                         .Cast<Floor>())
            {
                yield return ExistingFloorKey(floor);
            }
        }

        private static string ExistingFloorKey(Floor floor)
        {
            BoundingBoxXYZ? boundingBox = floor.get_BoundingBox(null);
            if (boundingBox is null)
            {
                return $"{IdPart(floor.GetTypeId())}:no_bbox:{floor.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)}";
            }

            ElementId levelId = GetElementIdValue(floor, BuiltInParameter.LEVEL_PARAM) ?? ElementId.InvalidElementId;
            double heightAboveLevel = GetDoubleValue(floor, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM) ?? 0.0;

            List<XYZ> corners =
            [
                new XYZ(boundingBox.Min.X, boundingBox.Min.Y, 0.0),
                new XYZ(boundingBox.Max.X, boundingBox.Max.Y, 0.0),
            ];

            return FloorBoundsKey(floor.GetTypeId(), levelId, heightAboveLevel, corners);
        }

        private static string SegmentKey(ElementId typeId, XYZ first, XYZ second)
        {
            XYZ start = first;
            XYZ end = second;
            if (CompareXYZ(second, first) < 0)
            {
                start = second;
                end = first;
            }

            return $"{IdPart(typeId)}:{Fmt(start)}|{Fmt(end)}";
        }

        private static string FloorPolygonKey(string? sectionName, IReadOnlyList<XYZ> points)
        {
            string joined = string.Join("|", points.Select(Fmt));
            return $"{MapSectionName(sectionName)}:{joined}";
        }

        private static string FloorBoundsKey(
            ElementId floorTypeId,
            ElementId levelId,
            double heightAboveLevel,
            IReadOnlyList<XYZ> points)
        {
            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);
            double maxX = points.Max(point => point.X);
            double maxY = points.Max(point => point.Y);

            return $"{IdPart(floorTypeId)}:{IdPart(levelId)}:{R(heightAboveLevel)}:{R(minX)},{R(minY)}|{R(maxX)},{R(maxY)}";
        }

        private static int CompareXYZ(XYZ first, XYZ second)
        {
            int compareX = R(first.X).CompareTo(R(second.X));
            if (compareX != 0)
            {
                return compareX;
            }

            int compareY = R(first.Y).CompareTo(R(second.Y));
            if (compareY != 0)
            {
                return compareY;
            }

            return R(first.Z).CompareTo(R(second.Z));
        }

        private static string Fmt(XYZ point)
        {
            return $"{R(point.X)},{R(point.Y)},{R(point.Z)}";
        }

        private static double R(double value)
        {
            return Math.Round(value / GeometryToleranceFeet) * GeometryToleranceFeet;
        }

        private static string ElevationKey(double elevationFeet)
        {
            return R(elevationFeet).ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string IdPart(ElementId elementId)
        {
            return elementId == ElementId.InvalidElementId
                ? "invalid"
                : elementId.IntegerValue.ToString(CultureInfo.InvariantCulture);
        }

        private static ElementId? GetElementIdValue(Element element, BuiltInParameter parameterId)
        {
            try
            {
                Parameter? parameter = element.get_Parameter(parameterId);
                if (parameter is null || parameter.StorageType != StorageType.ElementId)
                {
                    return null;
                }

                return parameter.AsElementId();
            }
            catch
            {
                return null;
            }
        }

        private static double? GetDoubleValue(Element element, BuiltInParameter parameterId)
        {
            try
            {
                Parameter? parameter = element.get_Parameter(parameterId);
                if (parameter is null || parameter.StorageType != StorageType.Double)
                {
                    return null;
                }

                return parameter.AsDouble();
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class BuildSummary
    {
        public int BeamsCreated { get; set; }

        public int ColumnsCreated { get; set; }

        public int SlabsCreated { get; set; }

        public int LevelsCreated { get; set; }

        public int FloorTypesCreated { get; set; }

        public int MembersSkipped { get; set; }

        public int SlabsSkipped { get; set; }

        public int Errors { get; set; }

        public List<string> ErrorMessages { get; } = new();

        public void AddError(string message)
        {
            Errors++;

            if (ErrorMessages.Count < 5)
            {
                ErrorMessages.Add("- " + message);
            }
        }
    }
}
