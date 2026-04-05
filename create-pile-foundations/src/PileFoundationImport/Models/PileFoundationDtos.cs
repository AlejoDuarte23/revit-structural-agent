namespace PileFoundationImport.Models;

public sealed class PileFoundationRequest
{
    public string FamilyName { get; init; } = string.Empty;

    public string TypeName { get; init; } = string.Empty;

    public string? TargetTypeName { get; init; }

    public string Units { get; init; } = "Meters";

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }

    public PileFoundationTypeParameters Parameters { get; init; } = new();
}

public sealed class PileFoundationTypeParameters
{
    public double? FoundationThickness { get; init; }

    public double? WidthIndent { get; init; }

    public double? PileLength { get; init; }

    public double? PileDiameter { get; init; }

    public double? PileCentresVertical { get; init; }

    public double? PileCentresHorizontal { get; init; }

    public double? Length1 { get; init; }

    public double? Length2 { get; init; }

    public double? PileCutOut { get; init; }

    public double? Clearance { get; init; }

    public bool HasValues =>
        FoundationThickness.HasValue ||
        WidthIndent.HasValue ||
        PileLength.HasValue ||
        PileDiameter.HasValue ||
        PileCentresVertical.HasValue ||
        PileCentresHorizontal.HasValue ||
        Length1.HasValue ||
        Length2.HasValue ||
        PileCutOut.HasValue ||
        Clearance.HasValue;
}
