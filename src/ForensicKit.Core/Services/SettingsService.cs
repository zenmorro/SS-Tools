using System.Text.Json;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    string EffectiveToolsRoot { get; }
}

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public string EffectiveToolsRoot =>
        string.IsNullOrWhiteSpace(Current.DownloadRoot) ? _paths.DefaultToolsRoot : Current.DownloadRoot;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                var json = await File.ReadAllTextAsync(_paths.SettingsFile, ct).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch
        {
            // Corrupt settings must never prevent startup; fall back to defaults.
            Current = new AppSettings();
        }

        return Current;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(_paths.SettingsFile, json, ct).ConfigureAwait(false);
    }
}
