using System.Text.Json;
using System.Text.RegularExpressions;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public interface IGitHubReleaseService
{
    Task<ResolvedDownload> ResolveLatestAsync(ToolEntry tool, CancellationToken ct = default);
}

/// <summary>
/// Resolves the latest stable release asset for a GitHub-hosted tool via the
/// public Releases API (/repos/{owner}/{repo}/releases/latest).
/// </summary>
public sealed class GitHubReleaseService : IGitHubReleaseService
{
    private readonly IHttpClientFactoryLite _clientFactory;

    public GitHubReleaseService(IHttpClientFactoryLite clientFactory) => _clientFactory = clientFactory;

    public async Task<ResolvedDownload> ResolveLatestAsync(ToolEntry tool, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tool.Owner) || string.IsNullOrWhiteSpace(tool.Repo))
            throw new InvalidOperationException(
                $"Tool '{tool.Id}' is marked github_release but is missing owner/repo.");

        var api = $"https://api.github.com/repos/{tool.Owner}/{tool.Repo}/releases/latest";

        using var client = _clientFactory.Create();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ForensicKit");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.GetAsync(api, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;

        if (!root.TryGetProperty("assets", out var assets) || assets.GetArrayLength() == 0)
            throw new InvalidOperationException(
                $"Release for '{tool.Owner}/{tool.Repo}' has no downloadable assets.");

        Regex? pattern = string.IsNullOrWhiteSpace(tool.AssetPattern)
            ? null
            : new Regex(tool.AssetPattern, RegexOptions.IgnoreCase);

        string? chosenUrl = null;
        string? chosenName = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || url is null)
                continue;

            var isCandidate = pattern is not null
                ? pattern.IsMatch(name)
                : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            if (isCandidate)
            {
                chosenUrl = url;
                chosenName = name;
                break;
            }
        }

        if (chosenUrl is null || chosenName is null)
            throw new InvalidOperationException(
                $"No matching asset found for '{tool.Owner}/{tool.Repo}'" +
                (pattern is not null ? $" using pattern '{tool.AssetPattern}'." : "."));

        return new ResolvedDownload(chosenUrl, chosenName, tag, tool.Sha256);
    }
}
