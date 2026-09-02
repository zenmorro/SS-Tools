namespace ForensicKit.Core.Infrastructure;

/// <summary>
/// Centralized, testable resolution of the folders and files ForensicKit uses.
/// Everything lives under %APPDATA%\ForensicKit unless overridden.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "ForensicKit";

    public AppPaths(string? rootOverride = null)
    {
        Root = string.IsNullOrWhiteSpace(rootOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName)
            : rootOverride;

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DefaultToolsRoot);
        Directory.CreateDirectory(LogsFolder);
    }

    /// <summary>Base folder, %APPDATA%\ForensicKit.</summary>
    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LocalManifestFile => Path.Combine(Root, "tools.json");

    /// <summary>Default place to store downloaded tools, unless the user picks another folder.</summary>
    public string DefaultToolsRoot => Path.Combine(Root, "Tools");

    public string LogsFolder => Path.Combine(Root, "Logs");

    /// <summary>Append-only audit log (JSON Lines).</summary>
    public string AuditLogFile => Path.Combine(LogsFolder, "audit.log.jsonl");

    /// <summary>Per-tool install folder under the (possibly user-overridden) tools root.</summary>
    public string ToolFolder(string toolsRoot, string toolId) =>
        Path.Combine(string.IsNullOrWhiteSpace(toolsRoot) ? DefaultToolsRoot : toolsRoot, toolId);

    /// <summary>Where a tool's install metadata (resolved version, hash) is stored.</summary>
    public string ToolStateFile(string toolsRoot, string toolId) =>
        Path.Combine(ToolFolder(toolsRoot, toolId), ".forensickit.json");
}
