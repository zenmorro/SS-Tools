using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// A single tool entry as defined in the manifest (tools.json).
/// This type maps 1:1 to the documented JSON schema and is intentionally
/// free of any UI or runtime state.
/// </summary>
public sealed class ToolEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "Uncategorized";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("sourceType")]
    public SourceType SourceType { get; set; } = SourceType.DirectUrl;

    /// <summary>Direct download link (used when <see cref="SourceType"/> is DirectUrl).</summary>
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    /// <summary>GitHub owner (used when <see cref="SourceType"/> is GithubRelease).</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>GitHub repository name (used when <see cref="SourceType"/> is GithubRelease).</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; set; }

    /// <summary>
    /// Optional regex used to pick the right asset among a GitHub release's assets
    /// (e.g. "win.*x64.*\\.zip$"). If null the first archive/exe asset is used.
    /// </summary>
    [JsonPropertyName("assetPattern")]
    public string? AssetPattern { get; set; }

    /// <summary>Optional expected SHA-256 (hex). When present the download is verified.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("packageType")]
    public PackageType PackageType { get; set; } = PackageType.Zip;

    /// <summary>Executable to launch, relative to the tool's install folder.</summary>
    [JsonPropertyName("executable")]
    public string Executable { get; set; } = string.Empty;

    [JsonPropertyName("requiresElevation")]
    public bool RequiresElevation { get; set; }

    /// <summary>Default command-line arguments; the user may override before launch.</summary>
    [JsonPropertyName("args")]
    public string Args { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// Optional declared version. Used for update comparison for DirectUrl tools
    /// (GitHub tools compare against the release tag instead).
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
