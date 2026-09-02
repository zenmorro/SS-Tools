using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// Progress information reported during a download.
/// </summary>
public readonly record struct DownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond)
{
    public double? Percentage =>
        TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100.0 : null;
}

/// <summary>
/// Result of resolving a download source into a concrete URL + expected metadata.
/// </summary>
public sealed record ResolvedDownload(
    string Url,
    string FileName,
    string? Version,
    string? Sha256);

/// <summary>
/// Result of a completed download + extraction operation.
/// </summary>
public sealed record InstallResult(
    string InstallFolder,
    string ExecutablePath,
    string? ResolvedVersion,
    string ArchiveSha256);

/// <summary>
/// Authenticode signature verification outcome for an executable.
/// </summary>
public sealed record SignatureInfo(
    bool IsSigned,
    string? Subject,
    string? Issuer,
    DateTime? NotBefore,
    DateTime? NotAfter,
    string StatusMessage);

/// <summary>
/// A single append-only audit record. Written once, never mutated.
/// </summary>
public sealed class ExecutionLogEntry
{
    [JsonPropertyName("timestampUtc")]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("user")]
    public string User { get; set; } = Environment.UserName;

    [JsonPropertyName("machine")]
    public string Machine { get; set; } = Environment.MachineName;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty; // "download" | "execute" | "script" | "info"

    [JsonPropertyName("toolId")]
    public string ToolId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public ExecutionResult Result { get; set; }

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}

/// <summary>
/// Metadata describing an embedded PowerShell script exposed in the UI.
/// </summary>
public sealed class ScriptDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>Manifest resource name of the embedded .ps1.</summary>
    public string ResourceName { get; init; } = string.Empty;

    /// <summary>Whether the script needs administrator rights to gather everything.</summary>
    public bool RequiresElevation { get; init; }
}
