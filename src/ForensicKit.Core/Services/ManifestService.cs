using System.Text.Json;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public sealed record ManifestLoadResult(
    ToolManifest Manifest,
    string Source,      // "remote", "local", "bundled"
    string? Warning);

public interface IManifestService
{
    /// <summary>
    /// Loads the catalog: tries the remote manifest (if enabled), validates it, and on
    /// success caches it locally. Falls back to the cached local copy, then to the
    /// bundled default, so the app always has a usable catalog even offline.
    /// </summary>
    Task<ManifestLoadResult> LoadAsync(CancellationToken ct = default);

    /// <summary>Validates a manifest; returns the list of problems (empty = valid).</summary>
    IReadOnlyList<string> Validate(ToolManifest? manifest);
}

public sealed class ManifestService : IManifestService
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly IHttpClientFactoryLite _clientFactory;
    private readonly Func<string> _bundledManifestProvider;

    public ManifestService(
        AppPaths paths,
        ISettingsService settings,
        IHttpClientFactoryLite clientFactory,
        Func<string> bundledManifestProvider)
    {
        _paths = paths;
        _settings = settings;
        _clientFactory = clientFactory;
        _bundledManifestProvider = bundledManifestProvider;
    }

    public async Task<ManifestLoadResult> LoadAsync(CancellationToken ct = default)
    {
        // 1) Try remote.
        if (_settings.Current.AutoUpdateCatalog &&
            !string.IsNullOrWhiteSpace(_settings.Current.ManifestUrl))
        {
            try
            {
                using var client = _clientFactory.Create();
                var json = await client.GetStringAsync(_settings.Current.ManifestUrl, ct)
                    .ConfigureAwait(false);

                var manifest = Deserialize(json);
                var problems = Validate(manifest);
                if (problems.Count == 0 && manifest is not null)
                {
                    // Cache the validated manifest for offline use.
                    await File.WriteAllTextAsync(_paths.LocalManifestFile, json, ct).ConfigureAwait(false);
                    return new ManifestLoadResult(manifest, "remote", null);
                }

                // Remote manifest was malformed: do NOT apply it.
                return await LoadFallbackAsync(
                    $"Remote manifest rejected ({string.Join("; ", problems)}). Using local copy.", ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return await LoadFallbackAsync($"Remote manifest unavailable ({ex.Message}).", ct)
                    .ConfigureAwait(false);
            }
        }

        return await LoadFallbackAsync(null, ct).ConfigureAwait(false);
    }

    private async Task<ManifestLoadResult> LoadFallbackAsync(string? warning, CancellationToken ct)
    {
        // 2) Try cached local file.
        if (File.Exists(_paths.LocalManifestFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_paths.LocalManifestFile, ct).ConfigureAwait(false);
                var manifest = Deserialize(json);
                if (manifest is not null && Validate(manifest).Count == 0)
                    return new ManifestLoadResult(manifest, "local", warning);
            }
            catch { /* fall through to bundled */ }
        }

        // 3) Bundled default that ships with the app.
        var bundled = Deserialize(_bundledManifestProvider())
            ?? new ToolManifest();
        return new ManifestLoadResult(bundled, "bundled", warning);
    }

    public IReadOnlyList<string> Validate(ToolManifest? manifest)
    {
        var problems = new List<string>();

        if (manifest is null)
        {
            problems.Add("Manifest is null or could not be parsed.");
            return problems;
        }

        if (manifest.SchemaVersion > SupportedSchemaVersion)
            problems.Add(
                $"Schema version {manifest.SchemaVersion} is newer than supported ({SupportedSchemaVersion}).");

        if (manifest.Tools.Count == 0)
            problems.Add("Manifest contains no tools.");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in manifest.Tools)
        {
            var label = string.IsNullOrWhiteSpace(tool.Id) ? "(missing id)" : tool.Id;

            if (string.IsNullOrWhiteSpace(tool.Id))
                problems.Add("A tool is missing its 'id'.");
            else if (!seenIds.Add(tool.Id))
                problems.Add($"Duplicate tool id '{tool.Id}'.");

            if (string.IsNullOrWhiteSpace(tool.Name))
                problems.Add($"Tool '{label}' is missing 'name'.");

            if (string.IsNullOrWhiteSpace(tool.Executable) && tool.PackageType == PackageType.Zip)
                problems.Add($"Tool '{label}' is a zip but has no 'executable'.");

            switch (tool.SourceType)
            {
                case SourceType.DirectUrl when string.IsNullOrWhiteSpace(tool.DownloadUrl):
                    problems.Add($"Tool '{label}' is direct_url but has no 'downloadUrl'.");
                    break;
                case SourceType.GithubRelease
                    when string.IsNullOrWhiteSpace(tool.Owner) || string.IsNullOrWhiteSpace(tool.Repo):
                    problems.Add($"Tool '{label}' is github_release but is missing 'owner'/'repo'.");
                    break;
            }
        }

        return problems;
    }

    private static ToolManifest? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ToolManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
