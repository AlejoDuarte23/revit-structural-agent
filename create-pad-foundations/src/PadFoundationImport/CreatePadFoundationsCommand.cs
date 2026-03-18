using System.Globalization;
using System.Text.Json;
using PadFoundationImport.Models;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using WinForms = System.Windows.Forms;

namespace PadFoundationImport;

[Transaction(TransactionMode.Manual)]
public sealed class CreatePadFoundationsCommand : IExternalCommand
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uiDocument = commandData.Application.ActiveUIDocument;
        Document document = uiDocument.Document;

        if (document.IsFamilyDocument)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pad Foundations",
                "Open a Revit project document before creating pad foundations.");
            return Result.Failed;
        }

        string? path = PromptForJsonPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Cancelled;
        }

        SettingsStore.SaveLastJsonPath(path);

        IReadOnlyList<PadFoundationRequest> requests;
        try
        {
            requests = ReadJson(path);
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pad Foundations",
                $"Failed to read JSON:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        if (requests.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pad Foundations",
                "The JSON file does not contain any footing requests.");
            return Result.Failed;
        }

        try
        {
            using Transaction transaction = new(document, "Create Pad Foundations");
            transaction.Start();

            FoundationBuilder builder = new(document);
            BuildSummary summary = builder.Create(requests);

            transaction.Commit();

            Autodesk.Revit.UI.TaskDialog.Show(
                "Pad Foundations",
                "Created:" + Environment.NewLine +
                $"- Footings: {summary.FootingsCreated}{Environment.NewLine}" +
                $"- Types: {summary.TypesCreated}{Environment.NewLine}{Environment.NewLine}" +
                "Matched:" + Environment.NewLine +
                $"- By node id: {summary.MatchedByNodeId}{Environment.NewLine}" +
                $"- By coordinates: {summary.MatchedByCoordinates}{Environment.NewLine}{Environment.NewLine}" +
                "Skipped:" + Environment.NewLine +
                $"- Missing columns: {summary.MissingColumns}{Environment.NewLine}" +
                $"- Duplicates: {summary.DuplicatesSkipped}{Environment.NewLine}{Environment.NewLine}" +
                $"Errors: {summary.Errors}" +
                (summary.ErrorMessages.Count == 0
                    ? string.Empty
                    : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, summary.ErrorMessages)));

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pad Foundations",
                $"Footing creation failed:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }
    }

    private static string? PromptForJsonPath()
    {
        string? lastPath = SettingsStore.LoadLastJsonPath();

        using WinForms.OpenFileDialog dialog = new();
        dialog.Title = "Select pad foundations JSON";
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

    private static IReadOnlyList<PadFoundationRequest> ReadJson(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
        JsonElement array = document.RootElement;
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("JSON root must be an array.");
        }

        List<PadFoundationRequest> requests = new();
        int index = 0;

        foreach (JsonElement item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Footing item {index} must be a JSON object.");
            }

            requests.Add(new PadFoundationRequest
            {
                NodeId = ReadRequiredInt(item, index, "node_id"),
                WidthMeters = ReadRequiredDouble(item, index, "B"),
                LengthMeters = ReadRequiredDouble(item, index, "L"),
                X = ReadRequiredDouble(item, index, "x"),
                Y = ReadRequiredDouble(item, index, "y"),
                Z = ReadRequiredDouble(item, index, "z"),
            });

            if (requests[^1].WidthMeters <= 0.0 || requests[^1].LengthMeters <= 0.0)
            {
                throw new InvalidOperationException($"Footing item {index} must have positive B and L dimensions.");
            }
        }

        return requests;
    }

    private static double ReadRequiredDouble(JsonElement item, int index, string name)
    {
        double? value = ReadOptionalDouble(item, name);
        if (!value.HasValue)
        {
            throw new InvalidOperationException(
                $"Footing item {index} is missing required numeric property '{name}'.");
        }

        return value.Value;
    }

    private static int ReadRequiredInt(JsonElement item, int index, string name)
    {
        int? value = ReadOptionalInt(item, name);
        if (!value.HasValue)
        {
            throw new InvalidOperationException(
                $"Footing item {index} is missing required integer property '{name}'.");
        }

        return value.Value;
    }

    private static double? ReadOptionalDouble(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out JsonElement value))
        {
            return null;
        }

        if (TryReadDouble(value, out double result))
        {
            return result;
        }

        return null;
    }

    private static int? ReadOptionalInt(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryReadDouble(JsonElement value, out double result)
    {
        result = default;

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        return false;
    }

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (JsonProperty property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class FoundationBuilder
    {
        private static readonly double MatchToleranceFeet =
            UnitUtils.ConvertToInternalUnits(10.0, UnitTypeId.Millimeters);

        private readonly Document _document;
        private readonly List<ColumnCandidate> _columns;
        private readonly Dictionary<string, FamilySymbol> _foundationTypesByName;
        private readonly HashSet<string> _existingFootingKeys;
        private readonly List<Level> _levels;
        private readonly FamilySymbol _templateType;

        public FoundationBuilder(Document document)
        {
            _document = document;
            _columns = CollectColumns(document);
            _foundationTypesByName = CollectFoundationTypes(document);
            _existingFootingKeys = CollectExistingFootingKeys(document);
            _levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ToList();

            _templateType = SelectTemplateType(_foundationTypesByName.Values);
        }

        public BuildSummary Create(IReadOnlyList<PadFoundationRequest> requests)
        {
            BuildSummary summary = new();

            foreach (PadFoundationRequest request in requests)
            {
                try
                {
                    MatchResult match = MatchColumn(request);
                    if (match.Column is null)
                    {
                        summary.MissingColumns++;
                        continue;
                    }

                    if (match.MatchKind == MatchKind.NodeId)
                    {
                        summary.MatchedByNodeId++;
                    }
                    else
                    {
                        summary.MatchedByCoordinates++;
                    }

                    double widthFeet = MetersToFeet(request.WidthMeters);
                    double lengthFeet = MetersToFeet(request.LengthMeters);
                    FamilySymbol symbol = GetOrCreateFoundationType(widthFeet, lengthFeet, summary);
                    Activate(symbol);

                    XYZ insertionPoint = match.Column.BasePoint;
                    string footingKey = FootingKey(insertionPoint, widthFeet, lengthFeet);
                    if (_existingFootingKeys.Contains(footingKey))
                    {
                        summary.DuplicatesSkipped++;
                        continue;
                    }

                    Level level = GetReferenceLevel(match.Column, insertionPoint.Z);
                    _document.Create.NewFamilyInstance(
                        insertionPoint,
                        symbol,
                        level,
                        StructuralType.Footing);

                    _existingFootingKeys.Add(footingKey);
                    summary.FootingsCreated++;
                }
                catch (Exception ex)
                {
                    summary.AddError(ex.Message);
                }
            }

            return summary;
        }

        private MatchResult MatchColumn(PadFoundationRequest request)
        {
            if (request.NodeId.HasValue)
            {
                ColumnCandidate? byId = _columns.FirstOrDefault(column => column.BaseNodeId == request.NodeId.Value);
                if (byId is not null)
                {
                    return new MatchResult(byId, MatchKind.NodeId);
                }
            }

            XYZ target = new(
                MetersToFeet(request.X),
                MetersToFeet(request.Y),
                MetersToFeet(request.Z));

            ColumnCandidate? byCoordinates = _columns
                .Select(column => new
                {
                    Column = column,
                    Distance = column.BasePoint.DistanceTo(target),
                })
                .Where(item => item.Distance <= MatchToleranceFeet)
                .OrderBy(item => item.Distance)
                .Select(item => item.Column)
                .FirstOrDefault();

            return new MatchResult(byCoordinates, byCoordinates is null ? MatchKind.None : MatchKind.Coordinates);
        }

        private FamilySymbol GetOrCreateFoundationType(double widthFeet, double lengthFeet, BuildSummary summary)
        {
            string typeName = BuildTypeName(widthFeet, lengthFeet);
            if (_foundationTypesByName.TryGetValue(typeName, out FamilySymbol? existing))
            {
                return existing;
            }

            FamilySymbol duplicated = (FamilySymbol)_templateType.Duplicate(typeName);

            bool widthSet = TrySetLengthLikeParameter(
                duplicated,
                widthFeet,
                exactMatches: new[] { "width", "foundationwidth", "overallwidth", "ancho", "bx", "b" },
                containsMatches: new[] { "width", "ancho" });

            bool lengthSet = TrySetLengthLikeParameter(
                duplicated,
                lengthFeet,
                exactMatches: new[] { "length", "foundationlength", "overalllength", "largo", "by", "l" },
                containsMatches: new[] { "length", "largo" });

            if (!widthSet)
            {
                throw new InvalidOperationException(
                    $"The structural foundation family type '{duplicated.Name}' does not expose an editable width parameter.");
            }

            if (!lengthSet && !NearlyEqual(widthFeet, lengthFeet))
            {
                throw new InvalidOperationException(
                    $"The structural foundation family type '{duplicated.Name}' does not expose an editable length parameter.");
            }

            _foundationTypesByName[typeName] = duplicated;
            summary.TypesCreated++;
            return duplicated;
        }

        private static bool TrySetLengthLikeParameter(
            Element element,
            double value,
            IReadOnlyCollection<string> exactMatches,
            IReadOnlyCollection<string> containsMatches)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                {
                    continue;
                }

                string normalized = NormalizeParameterName(parameter.Definition?.Name);
                if (exactMatches.Contains(normalized))
                {
                    return parameter.Set(value);
                }
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                {
                    continue;
                }

                string normalized = NormalizeParameterName(parameter.Definition?.Name);
                if (containsMatches.Any(normalized.Contains))
                {
                    return parameter.Set(value);
                }
            }

            return false;
        }

        private static string NormalizeParameterName(string? name)
        {
            return new string((name ?? string.Empty)
                .ToLowerInvariant()
                .Where(char.IsLetter)
                .ToArray());
        }

        private Level GetReferenceLevel(ColumnCandidate column, double elevationFeet)
        {
            if (column.BaseLevelId != ElementId.InvalidElementId &&
                _document.GetElement(column.BaseLevelId) is Level baseLevel)
            {
                return baseLevel;
            }

            Level? existing = _levels
                .FirstOrDefault(level => NearlyEqual(level.Elevation, elevationFeet));

            if (existing is not null)
            {
                return existing;
            }

            Level created = Level.Create(_document, elevationFeet);
            _levels.Add(created);
            _levels.Sort((first, second) => first.Elevation.CompareTo(second.Elevation));
            return created;
        }

        private static FamilySymbol SelectTemplateType(IEnumerable<FamilySymbol> symbols)
        {
            FamilySymbol? template = symbols
                .FirstOrDefault();
            if (template is null)
            {
                throw new InvalidOperationException(
                    "No structural foundation family types are loaded in this Revit project.");
            }

            return template;
        }

        private static Dictionary<string, FamilySymbol> CollectFoundationTypes(Document document)
        {
            Dictionary<string, FamilySymbol> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (FamilySymbol symbol in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                         .WhereElementIsElementType()
                         .Cast<Element>()
                         .OfType<FamilySymbol>())
            {
                if (!result.ContainsKey(symbol.Name))
                {
                    result.Add(symbol.Name, symbol);
                }
            }

            return result;
        }

        private static List<ColumnCandidate> CollectColumns(Document document)
        {
            List<ColumnCandidate> result = new();

            foreach (FamilyInstance column in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                if (!TryGetColumnBasePoint(column, out XYZ? basePoint))
                {
                    continue;
                }

                int? baseNodeId = ColumnSourceNodeStorage.TryGetBaseNodeId(column, out int nodeId)
                    ? nodeId
                    : null;

                ElementId baseLevelId = GetElementIdValue(column, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)
                    ?? ElementId.InvalidElementId;

                result.Add(new ColumnCandidate(column, basePoint, baseNodeId, baseLevelId));
            }

            return result;
        }

        private static bool TryGetColumnBasePoint(FamilyInstance column, out XYZ? basePoint)
        {
            basePoint = null;

            if (column.Location is LocationCurve locationCurve && locationCurve.Curve is Line line)
            {
                XYZ first = line.GetEndPoint(0);
                XYZ second = line.GetEndPoint(1);
                basePoint = first.Z <= second.Z ? first : second;
                return true;
            }

            if (column.Location is LocationPoint locationPoint)
            {
                basePoint = locationPoint.Point;
                return true;
            }

            BoundingBoxXYZ? boundingBox = column.get_BoundingBox(null);
            if (boundingBox is null)
            {
                return false;
            }

            basePoint = new XYZ(
                (boundingBox.Min.X + boundingBox.Max.X) * 0.5,
                (boundingBox.Min.Y + boundingBox.Max.Y) * 0.5,
                boundingBox.Min.Z);
            return true;
        }

        private static HashSet<string> CollectExistingFootingKeys(Document document)
        {
            HashSet<string> keys = new(StringComparer.Ordinal);

            foreach (FamilyInstance foundation in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                         .OfClass(typeof(FamilyInstance))
                         .Cast<FamilyInstance>())
            {
                if (!TryGetFoundationLocation(foundation, out XYZ? point))
                {
                    continue;
                }

                double width = GetLengthLikeParameter(
                    foundation.Symbol,
                    exactMatches: new[] { "width", "foundationwidth", "overallwidth", "ancho", "bx", "b" },
                    containsMatches: new[] { "width", "ancho" });
                double length = GetLengthLikeParameter(
                    foundation.Symbol,
                    exactMatches: new[] { "length", "foundationlength", "overalllength", "largo", "by", "l" },
                    containsMatches: new[] { "length", "largo" });

                keys.Add(FootingKey(point, width, length <= 0.0 ? width : length));
            }

            return keys;
        }

        private static bool TryGetFoundationLocation(FamilyInstance foundation, out XYZ? point)
        {
            point = null;

            if (foundation.Location is LocationPoint locationPoint)
            {
                point = locationPoint.Point;
                return true;
            }

            BoundingBoxXYZ? boundingBox = foundation.get_BoundingBox(null);
            if (boundingBox is null)
            {
                return false;
            }

            point = new XYZ(
                (boundingBox.Min.X + boundingBox.Max.X) * 0.5,
                (boundingBox.Min.Y + boundingBox.Max.Y) * 0.5,
                boundingBox.Min.Z);
            return true;
        }

        private static double GetLengthLikeParameter(
            Element element,
            IReadOnlyCollection<string> exactMatches,
            IReadOnlyCollection<string> containsMatches)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.StorageType != StorageType.Double)
                {
                    continue;
                }

                string normalized = NormalizeParameterName(parameter.Definition?.Name);
                if (exactMatches.Contains(normalized))
                {
                    return parameter.AsDouble();
                }
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.StorageType != StorageType.Double)
                {
                    continue;
                }

                string normalized = NormalizeParameterName(parameter.Definition?.Name);
                if (containsMatches.Any(normalized.Contains))
                {
                    return parameter.AsDouble();
                }
            }

            return 0.0;
        }

        private void Activate(FamilySymbol symbol)
        {
            if (symbol.IsActive)
            {
                return;
            }

            symbol.Activate();
            _document.Regenerate();
        }

        private static string BuildTypeName(double widthFeet, double lengthFeet)
        {
            double widthMm = UnitUtils.ConvertFromInternalUnits(widthFeet, UnitTypeId.Millimeters);
            double lengthMm = UnitUtils.ConvertFromInternalUnits(lengthFeet, UnitTypeId.Millimeters);
            return $"Pad {Math.Round(widthMm):0}x{Math.Round(lengthMm):0} mm";
        }

        private static string FootingKey(XYZ point, double widthFeet, double lengthFeet)
        {
            return $"{Fmt(point)}:{RoundToTolerance(widthFeet):0.########}:{RoundToTolerance(lengthFeet):0.########}";
        }

        private static string Fmt(XYZ point)
        {
            return $"{RoundToTolerance(point.X):0.########},{RoundToTolerance(point.Y):0.########},{RoundToTolerance(point.Z):0.########}";
        }

        private static double RoundToTolerance(double value)
        {
            return Math.Round(value / MatchToleranceFeet) * MatchToleranceFeet;
        }

        private static bool NearlyEqual(double first, double second)
        {
            return Math.Abs(first - second) <= MatchToleranceFeet;
        }

        private static double MetersToFeet(double meters)
        {
            return UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
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
    }

    private sealed record ColumnCandidate(
        FamilyInstance Column,
        XYZ BasePoint,
        int? BaseNodeId,
        ElementId BaseLevelId);

    private sealed record MatchResult(ColumnCandidate? Column, MatchKind MatchKind);

    private enum MatchKind
    {
        None,
        NodeId,
        Coordinates,
    }

    private sealed class BuildSummary
    {
        public int FootingsCreated { get; set; }

        public int TypesCreated { get; set; }

        public int MatchedByNodeId { get; set; }

        public int MatchedByCoordinates { get; set; }

        public int MissingColumns { get; set; }

        public int DuplicatesSkipped { get; set; }

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
