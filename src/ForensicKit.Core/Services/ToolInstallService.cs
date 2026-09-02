using System.Text.Json;
using System.Text.Json.Serialization;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

/// <summary>Per-tool install metadata persisted next to the extracted files.</summary>
public sealed class ToolInstallState
{
    [JsonPropertyName("toolId")] public string ToolId { get; set; } = string.Empty;
    [JsonPropertyName("installedVersion")] public string? InstalledVersion { get; set; }
    [JsonPropertyName("archiveSha256")] public string ArchiveSha256 { get; set; } = string.Empty;
    [JsonPropertyName("executablePath")] public string ExecutablePath { get; set; } = string.Empty;
    [JsonPropertyName("sourceUrl")] public string SourceUrl { get; set; } = string.Empty;
    [JsonPropertyName("installedUtc")] public DateTime InstalledUtc { get; set; }
}

public interface IToolInstallService
{
    bool IsInstalled(ToolEntry tool);
    ToolInstallState? ReadState(ToolEntry tool);
    string? ResolveExecutablePath(ToolEntry tool);

    Task<InstallResult> InstallAsync(
        ToolEntry tool,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Returns (updateAvailable, latestVersion). Never throws on network errors.</summary>
    Task<(bool updateAvailable, string? latestVersion)> CheckForUpdateAsync(
        ToolEntry tool, CancellationToken ct = default);
}

/// <summary>
/// Coordinates the full acquisition pipeline for a tool:
/// resolve source -> download (progress/resume/retry) -> SHA-256 verify ->
/// extract (if zip) -> locate executable -> persist install state.
/// </summary>
public sealed class ToolInstallService : IToolInstallService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly IDownloadService _download;
    private readonly IExtractionService _extraction;
    private readonly IHashService _hash;
    private readonly IGitHubReleaseService _github;

    public ToolInstallService(
        AppPaths paths,
        ISettingsService settings,
        IDownloadService download,
        IExtractionService extraction,
        IHashService hash,
        IGitHubReleaseService github)
    {
        _paths = paths;
        _settings = settings;
        _download = download;
        _extraction = extraction;
        _hash = hash;
        _github = github;
    }

    private string ToolsRoot => _settings.EffectiveToolsRoot;

    public bool IsInstalled(ToolEntry tool) => ResolveExecutablePath(tool) is { } p && File.Exists(p);

    public ToolInstallState? ReadState(ToolEntry tool)
    {
        var stateFile = _paths.ToolStateFile(ToolsRoot, tool.Id);
        if (!File.Exists(stateFile))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ToolInstallState>(File.ReadAllText(stateFile));
        }
        catch
        {
            return null;
        }
    }

    public string? ResolveExecutablePath(ToolEntry tool)
    {
        var folder = _paths.ToolFolder(ToolsRoot, tool.Id);
        if (!Directory.Exists(folder))
            return null;

        // Preferred: the executable named in the manifest.
        if (!string.IsNullOrWhiteSpace(tool.Executable))
        {
            var direct = Path.Combine(folder, tool.Executable);
            if (File.Exists(direct))
                return direct;

            // Some archives nest a versioned subfolder; search for the exe by name.
            var byName = Directory
                .EnumerateFiles(folder, Path.GetFileName(tool.Executable), SearchOption.AllDirectories)
                .FirstOrDefault();
            if (byName is not null)
                return byName;
        }

        return null;
    }

    public async Task<InstallResult> InstallAsync(
        ToolEntry tool,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tool, ct).ConfigureAwait(false);

        var folder = _paths.ToolFolder(ToolsRoot, tool.Id);
        Directory.CreateDirectory(folder);

        var downloadDir = Path.Combine(folder, "_download");
        Directory.CreateDirectory(downloadDir);
        var archivePath = Path.Combine(downloadDir, resolved.FileName);

        await _download.DownloadAsync(resolved.Url, archivePath, progress, maxRetries: 3, ct)
            .ConfigureAwait(false);

        // Integrity verification (only enforced when a hash is declared).
        var actualHash = await _hash.ComputeSha256Async(archivePath, ct).ConfigureAwait(false);
        if (!_hash.Matches(actualHash, resolved.Sha256))
        {
            throw new InvalidOperationException(
                $"SHA-256 mismatch for '{tool.Name}'.\nExpected: {resolved.Sha256}\nActual:   {actualHash}\n" +
                "The download was NOT installed.");
        }

        string executablePath;

        if (tool.PackageType == PackageType.Zip)
        {
            await _extraction.ExtractZipAsync(archivePath, folder, ct).ConfigureAwait(false);
            executablePath = ResolveExecutablePath(tool)
                ?? throw new FileNotFoundException(
                    $"Extracted '{tool.Name}' but could not find '{tool.Executable}' in {folder}.");
        }
        else
        {
            var exeName = string.IsNullOrWhiteSpace(tool.Executable)
                ? resolved.FileName
                : tool.Executable;
            executablePath = Path.Combine(folder, exeName);
            File.Copy(archivePath, executablePath, overwrite: true);
        }

        var state = new ToolInstallState
        {
            ToolId = tool.Id,
            InstalledVersion = resolved.Version ?? tool.Version,
            ArchiveSha256 = actualHash,
            ExecutablePath = executablePath,
            SourceUrl = resolved.Url,
            InstalledUtc = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(
            _paths.ToolStateFile(ToolsRoot, tool.Id),
            JsonSerializer.Serialize(state, JsonOptions), ct).ConfigureAwait(false);

        // Clean up the intermediate download.
        try { Directory.Delete(downloadDir, recursive: true); } catch { /* best effort */ }

        return new InstallResult(folder, executablePath, state.InstalledVersion, actualHash);
    }

    public async Task<(bool updateAvailable, string? latestVersion)> CheckForUpdateAsync(
        ToolEntry tool, CancellationToken ct = default)
    {
        var state = ReadState(tool);
        if (state is null)
            return (false, null); // not installed -> "download", not "update"

        try
        {
            if (tool.SourceType == SourceType.GithubRelease)
            {
                var resolved = await _github.ResolveLatestAsync(tool, ct).ConfigureAwait(false);
                var available = !string.Equals(
                    resolved.Version?.Trim(), state.InstalledVersion?.Trim(),
                    StringComparison.OrdinalIgnoreCase);
                return (available, resolved.Version);
            }

            // DirectUrl: compare declared manifest version against installed version.
            var manifestVersion = tool.Version?.Trim();
            if (string.IsNullOrEmpty(manifestVersion))
                return (false, null);

            var updateAvailable = !string.Equals(
                manifestVersion, state.InstalledVersion?.Trim(), StringComparison.OrdinalIgnoreCase);
            return (updateAvailable, manifestVersion);
        }
        catch
        {
            // Offline / API error -> don't claim an update.
            return (false, state.InstalledVersion);
        }
    }

    private async Task<ResolvedDownload> ResolveAsync(ToolEntry tool, CancellationToken ct)
    {
        switch (tool.SourceType)
        {
            case SourceType.GithubRelease:
                return await _github.ResolveLatestAsync(tool, ct).ConfigureAwait(false);

            case SourceType.DirectUrl:
                if (string.IsNullOrWhiteSpace(tool.DownloadUrl))
                    throw new InvalidOperationException(
                        $"Tool '{tool.Id}' is direct_url but has no downloadUrl.");
                var fileName = GetFileNameFromUrl(tool.DownloadUrl);
                return new ResolvedDownload(tool.DownloadUrl, fileName, tool.Version, tool.Sha256);

            default:
                throw new NotSupportedException($"Unknown source type: {tool.SourceType}");
        }
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var name = Path.GetFileName(new Uri(url).AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
        }
        catch
        {
            return "download.bin";
        }
    }
}
