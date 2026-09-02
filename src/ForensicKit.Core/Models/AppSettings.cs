using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// User-configurable settings, persisted as JSON in %APPDATA%\ForensicKit\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Root folder where downloaded tools are stored. Empty = default under %APPDATA%.</summary>
    [JsonPropertyName("downloadRoot")]
    public string DownloadRoot { get; set; } = string.Empty;

    /// <summary>Raw URL of the remote manifest used to refresh the catalog at startup.</summary>
    [JsonPropertyName("manifestUrl")]
    public string ManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/your-org/ForensicKit/main/manifests/tools.json";

    /// <summary>Whether to fetch the remote manifest on startup.</summary>
    [JsonPropertyName("autoUpdateCatalog")]
    public bool AutoUpdateCatalog { get; set; } = true;

    /// <summary>"Dark" or "Light".</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Dark";

    /// <summary>Accent color as #RRGGBB. Empty = system/default accent.</summary>
    [JsonPropertyName("accentColor")]
    public string AccentColor { get; set; } = string.Empty;

    /// <summary>Optional HTTP(S) proxy, e.g. http://proxy.corp:8080. Empty = system default.</summary>
    [JsonPropertyName("proxyUrl")]
    public string ProxyUrl { get; set; } = string.Empty;

    /// <summary>Ids of tools pinned as favorites.</summary>
    [JsonPropertyName("favorites")]
    public List<string> Favorites { get; set; } = new();

    /// <summary>Skip the pre-launch trust dialog after the user has acknowledged a tool once.</summary>
    [JsonPropertyName("rememberTrustAcknowledgement")]
    public bool RememberTrustAcknowledgement { get; set; } = true;

    /// <summary>Ids of tools the user already acknowledged in the trust dialog.</summary>
    [JsonPropertyName("acknowledgedTools")]
    public List<string> AcknowledgedTools { get; set; } = new();
}
