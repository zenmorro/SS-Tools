using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// Root object of the tools manifest (tools.json).
/// </summary>
public sealed class ToolManifest
{
    /// <summary>Schema version, so the app can reject incompatible manifests.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Free-form catalog version / date, shown in the UI.</summary>
    [JsonPropertyName("catalogVersion")]
    public string? CatalogVersion { get; set; }

    [JsonPropertyName("tools")]
    public List<ToolEntry> Tools { get; set; } = new();
}
