using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// How the tool's binary is obtained.
/// </summary>
[JsonConverter(typeof(SourceTypeJsonConverter))]
public enum SourceType
{
    /// <summary>Direct link to an archive/executable that must be updated manually in the manifest.</summary>
    DirectUrl,

    /// <summary>Hosted on GitHub; the latest stable release asset is resolved via the Releases API.</summary>
    GithubRelease
}

/// <summary>
/// What kind of payload the download is.
/// </summary>
[JsonConverter(typeof(PackageTypeJsonConverter))]
public enum PackageType
{
    /// <summary>A single executable, used as-is.</summary>
    Exe,

    /// <summary>A ZIP archive that must be extracted.</summary>
    Zip
}

/// <summary>
/// Local install/update state of a tool, shown as a badge in the UI.
/// </summary>
public enum ToolStatus
{
    NotDownloaded,
    Downloaded,
    UpdateAvailable
}

/// <summary>
/// Outcome of a single execution, recorded in the audit log.
/// </summary>
public enum ExecutionResult
{
    Started,
    Succeeded,
    Failed,
    Cancelled
}
