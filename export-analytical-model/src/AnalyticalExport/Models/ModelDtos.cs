using System.Text.Json.Serialization;

namespace AnalyticalExport.Models;

public sealed class ModelDto
{
    [JsonPropertyName("nodes")]
    public List<NodeDto> Nodes { get; set; } = new();

    [JsonPropertyName("lines")]
    public List<LineDto> Lines { get; set; } = new();

    [JsonPropertyName("areas")]
    public List<AreaDto> Areas { get; set; } = new();
}

public sealed class NodeDto
{
    [JsonPropertyName("node_id")]
    public int NodeId { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class LineDto
{
    [JsonPropertyName("line_id")]
    public int LineId { get; set; }

    [JsonPropertyName("Ni")]
    public int Ni { get; set; }

    [JsonPropertyName("Nj")]
    public int Nj { get; set; }

    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class AreaDto
{
    [JsonPropertyName("area_id")]
    public int AreaId { get; set; }

    [JsonPropertyName("nodes")]
    public List<int> Nodes { get; set; } = new();

    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object?>? Metadata { get; set; }
}
