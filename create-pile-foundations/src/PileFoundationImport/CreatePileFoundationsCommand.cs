using System.Globalization;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using PileFoundationImport.Models;
using WinForms = System.Windows.Forms;

namespace PileFoundationImport;

[Transaction(TransactionMode.Manual)]
public sealed class CreatePileFoundationsCommand : IExternalCommand
{
    private const string DefaultFamilyName = "Pile Cap-3 Round Pile";
    private const string DefaultTypeName = "Standard";
    private const int DefaultNumberOfPiles = 3;
    private const string DefaultUnits = "Meters";

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
                "Pile Foundations",
                "Open a Revit project document before creating pile foundations.");
            return Result.Failed;
        }

        string? path = PromptForJsonPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Cancelled;
        }

        SettingsStore.SaveLastJsonPath(path);

        IReadOnlyList<PileFoundationRequest> requests;
        try
        {
            requests = ReadJson(path);
        }
        catch (Exception ex)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pile Foundations",
                $"Failed to read JSON:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }

        if (requests.Count == 0)
        {
            Autodesk.Revit.UI.TaskDialog.Show(
                "Pile Foundations",
                "The JSON file does not contain any pile foundation requests.");
            return Result.Failed;
        }

        try
        {
            using Transaction transaction = new(document, "Create Pile Foundations");
            transaction.Start();

            FoundationBuilder builder = new(document);
            BuildSummary summary = builder.Create(requests);

            transaction.Commit();

            Autodesk.Revit.UI.TaskDialog.Show(
                "Pile Foundations",
                "Created:" + Environment.NewLine +
                $"- Foundations: {summary.FoundationsCreated}{Environment.NewLine}" +
                $"- Types: {summary.TypesCreated}{Environment.NewLine}{Environment.NewLine}" +
                $"Matched by coordinates: {summary.MatchedByCoordinates}{Environment.NewLine}{Environment.NewLine}" +
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
                "Pile Foundations",
                $"Pile foundation creation failed:{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return Result.Failed;
        }
    }

    private static string? PromptForJsonPath()
    {
        string? lastPath = SettingsStore.LoadLastJsonPath();

        using WinForms.OpenFileDialog dialog = new();
        dialog.Title = "Select pile foundations JSON";
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

    private static IReadOnlyList<PileFoundationRequest> ReadJson(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
        JsonElement root = document.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Array => ReadRequestsFromArray(root, new RequestDefaults()),
            JsonValueKind.Object => ReadRequestsFromObject(root),
            _ => throw new InvalidOperationException("JSON root must be an array or an object."),
        };
    }

    private static IReadOnlyList<PileFoundationRequest> ReadRequestsFromObject(JsonElement root)
    {
        RequestDefaults defaults = new()
        {
            FamilyName = ReadOptionalString(root, "familyName") ?? DefaultFamilyName,
            TypeName = ReadOptionalString(root, "typeName") ?? DefaultTypeName,
            TargetTypeName = ReadOptionalString(root, "targetTypeName"),
            Units = ReadOptionalString(root, "units") ?? DefaultUnits,
            Parameters = ReadParameters(root, "parameters", "Payload"),
        };

        if (!TryGetProperty(root, "placements", out JsonElement placements))
        {
            throw new InvalidOperationException("Payload object must include a 'placements' array.");
        }

        return ReadRequestsFromArray(placements, defaults);
    }

    private static IReadOnlyList<PileFoundationRequest> ReadRequestsFromArray(
        JsonElement array,
        RequestDefaults defaults)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Expected a JSON array of pile foundation placements.");
        }

        List<PileFoundationRequest> requests = new();
        int index = 0;

        foreach (JsonElement item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Pile foundation item {index} must be a JSON object.");
            }

            PileFoundationTypeParameters parameters = MergeParameters(
                defaults.Parameters,
                ReadParameters(item, "parameters", $"Pile foundation item {index}"));
            string typeName = ReadOptionalString(item, "typeName") ?? defaults.TypeName;
            string? targetTypeName = ReadOptionalString(item, "targetTypeName");
            targetTypeName ??= defaults.TargetTypeName;

            if (parameters.HasValues &&
                !string.IsNullOrWhiteSpace(targetTypeName) &&
                string.Equals(typeName, targetTypeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Pile foundation item {index} cannot use the same 'typeName' and 'targetTypeName' when 'parameters' are provided.");
            }

            requests.Add(new PileFoundationRequest
            {
                FamilyName = ReadOptionalString(item, "familyName") ?? defaults.FamilyName,
                TypeName = typeName,
                TargetTypeName = targetTypeName,
                Units = ReadOptionalString(item, "units") ?? defaults.Units,
                X = ReadRequiredDouble(item, index, "x"),
                Y = ReadRequiredDouble(item, index, "y"),
                Z = ReadRequiredDouble(item, index, "z"),
                Parameters = parameters,
            });
        }

        return requests;
    }

    private static PileFoundationTypeParameters ReadParameters(
        JsonElement item,
        string propertyName,
        string contextLabel)
    {
        if (!TryGetProperty(item, propertyName, out JsonElement parametersElement))
        {
            return new PileFoundationTypeParameters();
        }

        if (parametersElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{contextLabel} property '{propertyName}' must be a JSON object.");
        }

        return new PileFoundationTypeParameters
        {
            FoundationThickness = ReadOptionalDouble(parametersElement, "foundationThickness"),
            WidthIndent = ReadOptionalDouble(parametersElement, "widthIndent"),
            PileLength = ReadOptionalDouble(parametersElement, "pileLength"),
            PileDiameter = ReadOptionalDouble(parametersElement, "pileDiameter"),
            PileCentresVertical = ReadOptionalDouble(parametersElement, "pileCentresVertical"),
            PileCentresHorizontal = ReadOptionalDouble(parametersElement, "pileCentresHorizontal"),
            Length1 = ReadOptionalDouble(parametersElement, "length1"),
            Length2 = ReadOptionalDouble(parametersElement, "length2"),
            PileCutOut = ReadOptionalDouble(parametersElement, "pileCutOut"),
            Clearance = ReadOptionalDouble(parametersElement, "clearance"),
        };
    }

    private static PileFoundationTypeParameters MergeParameters(
        PileFoundationTypeParameters defaults,
        PileFoundationTypeParameters overrides)
    {
        return new PileFoundationTypeParameters
        {
            FoundationThickness = overrides.FoundationThickness ?? defaults.FoundationThickness,
            WidthIndent = overrides.WidthIndent ?? defaults.WidthIndent,
            PileLength = overrides.PileLength ?? defaults.PileLength,
            PileDiameter = overrides.PileDiameter ?? defaults.PileDiameter,
            PileCentresVertical = overrides.PileCentresVertical ?? defaults.PileCentresVertical,
            PileCentresHorizontal = overrides.PileCentresHorizontal ?? defaults.PileCentresHorizontal,
            Length1 = overrides.Length1 ?? defaults.Length1,
            Length2 = overrides.Length2 ?? defaults.Length2,
            PileCutOut = overrides.PileCutOut ?? defaults.PileCutOut,
            Clearance = overrides.Clearance ?? defaults.Clearance,
        };
    }

    private static double ReadRequiredDouble(JsonElement item, int index, string name)
    {
        double? value = ReadOptionalDouble(item, name);
        if (!value.HasValue)
        {
            throw new InvalidOperationException(
                $"Pile foundation item {index} is missing required numeric property '{name}'.");
        }

        return value.Value;
    }

    private static string? ReadOptionalString(JsonElement item, string name)
    {
        if (!TryGetProperty(item, name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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
        private readonly Dictionary<string, FamilySymbol> _foundationTypesByKey;
        private readonly HashSet<string> _existingFoundationKeys;
        private readonly List<Level> _levels;

        public FoundationBuilder(Document document)
        {
            _document = document;
            _columns = CollectColumns(document);
            _foundationTypesByKey = CollectFoundationTypes(document);
            _existingFoundationKeys = CollectExistingFoundationKeys(document);
            _levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ToList();
        }

        public BuildSummary Create(IReadOnlyList<PileFoundationRequest> requests)
        {
            BuildSummary summary = new();

            foreach (PileFoundationRequest request in requests)
            {
                try
                {
                    ColumnCandidate? column = MatchColumn(request);
                    if (column is null)
                    {
                        summary.MissingColumns++;
                        continue;
                    }

                    summary.MatchedByCoordinates++;

                    FamilySymbol symbol = GetOrCreateFoundationType(request, summary);
                    Activate(symbol);

                    XYZ insertionPoint = column.BasePoint;
                    string foundationKey = FoundationKey(insertionPoint);
                    if (_existingFoundationKeys.Contains(foundationKey))
                    {
                        summary.DuplicatesSkipped++;
                        continue;
                    }

                    Level level = GetReferenceLevel(column, insertionPoint.Z);
                    _document.Create.NewFamilyInstance(
                        insertionPoint,
                        symbol,
                        level,
                        StructuralType.Footing);

                    _existingFoundationKeys.Add(foundationKey);
                    summary.FoundationsCreated++;
                }
                catch (Exception ex)
                {
                    summary.AddError(ex.Message);
                }
            }

            return summary;
        }

        private ColumnCandidate? MatchColumn(PileFoundationRequest request)
        {
            XYZ target = new(
                ToInternalLength(request.X, request.Units),
                ToInternalLength(request.Y, request.Units),
                ToInternalLength(request.Z, request.Units));

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

            return byCoordinates;
        }

        private FamilySymbol GetOrCreateFoundationType(PileFoundationRequest request, BuildSummary summary)
        {
            string templateKey = TypeKey(request.FamilyName, request.TypeName);
            if (!_foundationTypesByKey.TryGetValue(templateKey, out FamilySymbol? template))
            {
                throw new InvalidOperationException(
                    $"The structural foundation family '{request.FamilyName}' type '{request.TypeName}' is not loaded in the Revit model.");
            }

            if (!request.Parameters.HasValues && string.IsNullOrWhiteSpace(request.TargetTypeName))
            {
                return template;
            }

            string targetTypeName = string.IsNullOrWhiteSpace(request.TargetTypeName)
                ? BuildTypeName(request)
                : request.TargetTypeName!.Trim();

            string targetKey = TypeKey(request.FamilyName, targetTypeName);
            if (_foundationTypesByKey.TryGetValue(targetKey, out FamilySymbol? existing))
            {
                return existing;
            }

            FamilySymbol duplicated = (FamilySymbol)template.Duplicate(targetTypeName);
            ApplyParameters(duplicated, request.Parameters, request.Units);

            _foundationTypesByKey[targetKey] = duplicated;
            summary.TypesCreated++;
            return duplicated;
        }

        private static void ApplyParameters(
            FamilySymbol symbol,
            PileFoundationTypeParameters parameters,
            string units)
        {
            SetIntegerParameter(symbol, DefaultNumberOfPiles, "Number of Piles");
            SetLengthParameter(symbol, parameters.FoundationThickness, units, "Foundation Thickness");
            SetLengthParameter(symbol, parameters.WidthIndent, units, "Width Indent");
            SetLengthParameter(symbol, parameters.PileLength, units, "Pile Length");
            SetLengthParameter(symbol, parameters.PileDiameter, units, "Pile Diameter");
            SetLengthParameter(symbol, parameters.PileCentresVertical, units, "Pile Centres Vertical");
            SetLengthParameter(symbol, parameters.PileCentresHorizontal, units, "Pile Centres Horizontal");
            SetLengthParameter(symbol, parameters.Length1, units, "Length 1");
            SetLengthParameter(symbol, parameters.Length2, units, "Length 2");
            SetLengthParameter(symbol, parameters.PileCutOut, units, "Pile Cut Out");
            SetLengthParameter(symbol, parameters.Clearance, units, "Clearance");
        }

        private static void SetIntegerParameter(Element element, int value, params string[] names)
        {
            Parameter parameter = GetWritableParameter(element, StorageType.Integer, names);
            parameter.Set(value);
        }

        private static void SetLengthParameter(Element element, double? value, string units, params string[] names)
        {
            if (!value.HasValue)
            {
                return;
            }

            Parameter parameter = GetWritableParameter(element, StorageType.Double, names);
            parameter.Set(ToInternalLength(value.Value, units));
        }

        private static Parameter GetWritableParameter(Element element, StorageType storageType, IReadOnlyCollection<string> names)
        {
            Dictionary<string, string> normalizedNames = names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => NormalizeParameterName(name), name => name, StringComparer.OrdinalIgnoreCase);

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.IsReadOnly || parameter.StorageType != storageType)
                {
                    continue;
                }

                string normalized = NormalizeParameterName(parameter.Definition?.Name);
                if (normalizedNames.ContainsKey(normalized))
                {
                    return parameter;
                }
            }

            throw new InvalidOperationException(
                $"Type '{element.Name}' does not expose a writable parameter matching: {string.Join(", ", names)}.");
        }

        private static string NormalizeParameterName(string? name)
        {
            return new string((name ?? string.Empty)
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
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

        private static Dictionary<string, FamilySymbol> CollectFoundationTypes(Document document)
        {
            Dictionary<string, FamilySymbol> result = new(StringComparer.OrdinalIgnoreCase);

            foreach (FamilySymbol symbol in new FilteredElementCollector(document)
                         .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                         .WhereElementIsElementType()
                         .Cast<Element>()
                         .OfType<FamilySymbol>())
            {
                string key = TypeKey(symbol.FamilyName, symbol.Name);
                if (!result.ContainsKey(key))
                {
                    result.Add(key, symbol);
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

                ElementId baseLevelId = GetElementIdValue(column, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)
                    ?? ElementId.InvalidElementId;

                result.Add(new ColumnCandidate(column, basePoint!, baseLevelId));
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

        private static HashSet<string> CollectExistingFoundationKeys(Document document)
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

                keys.Add(FoundationKey(point!));
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

        private void Activate(FamilySymbol symbol)
        {
            if (symbol.IsActive)
            {
                return;
            }

            symbol.Activate();
            _document.Regenerate();
        }

        private static string BuildTypeName(PileFoundationRequest request)
        {
            StringBuilder builder = new();
            builder.Append(request.TypeName.Trim());

            AppendLength(builder, "FT", request.Parameters.FoundationThickness, request.Units);
            AppendLength(builder, "WI", request.Parameters.WidthIndent, request.Units);
            AppendLength(builder, "PL", request.Parameters.PileLength, request.Units);
            AppendLength(builder, "PD", request.Parameters.PileDiameter, request.Units);
            AppendLength(builder, "PV", request.Parameters.PileCentresVertical, request.Units);
            AppendLength(builder, "PH", request.Parameters.PileCentresHorizontal, request.Units);
            AppendLength(builder, "L1", request.Parameters.Length1, request.Units);
            AppendLength(builder, "L2", request.Parameters.Length2, request.Units);
            AppendLength(builder, "PC", request.Parameters.PileCutOut, request.Units);
            AppendLength(builder, "CL", request.Parameters.Clearance, request.Units);

            return builder.ToString();
        }

        private static void AppendLength(StringBuilder builder, string prefix, double? value, string units)
        {
            if (!value.HasValue)
            {
                return;
            }

            double internalValue = ToInternalLength(value.Value, units);
            double millimeters = UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters);

            builder.Append(" - ");
            builder.Append(prefix);
            builder.Append(Math.Round(millimeters).ToString("0", CultureInfo.InvariantCulture));
        }

        private static string TypeKey(string familyName, string typeName)
        {
            return $"{familyName.Trim()}::{typeName.Trim()}";
        }

        private static string FoundationKey(XYZ point)
        {
            return Fmt(point);
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

        private static double ToInternalLength(double value, string units)
        {
            return UnitUtils.ConvertToInternalUnits(value, GetLengthUnit(units));
        }

        private static ForgeTypeId GetLengthUnit(string units)
        {
            string normalized = (units ?? string.Empty).Trim().ToLowerInvariant();

            return normalized switch
            {
                "mm" or "millimeter" or "millimeters" => UnitTypeId.Millimeters,
                "cm" or "centimeter" or "centimeters" => UnitTypeId.Centimeters,
                "m" or "meter" or "meters" => UnitTypeId.Meters,
                "in" or "inch" or "inches" => UnitTypeId.Inches,
                "ft" or "foot" or "feet" => UnitTypeId.Feet,
                _ => throw new InvalidOperationException(
                    $"Unsupported length unit '{units}'. Use Millimeters, Centimeters, Meters, Inches, or Feet.")
            };
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
        ElementId BaseLevelId);

    private sealed class BuildSummary
    {
        public int FoundationsCreated { get; set; }

        public int TypesCreated { get; set; }

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

    private sealed class RequestDefaults
    {
        public string FamilyName { get; init; } = DefaultFamilyName;

        public string TypeName { get; init; } = DefaultTypeName;

        public string? TargetTypeName { get; init; }

        public string Units { get; init; } = DefaultUnits;

        public PileFoundationTypeParameters Parameters { get; init; } = new();
    }
}
