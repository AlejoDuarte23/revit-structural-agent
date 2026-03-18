namespace PadFoundationImport.Models;

public sealed class PadFoundationRequest
{
    public int? NodeId { get; init; }

    public double WidthMeters { get; init; }

    public double LengthMeters { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }
}
