using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.App.Services;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;

namespace ForensicKit.App.ViewModels;

/// <summary>One record of the anti-forensic timeline (bound to the DataGrid rows).</summary>
public sealed class TimelineEntry
{
    public DateTime? Timestamp { get; init; }
    public string TimeText => Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    public string Severity { get; init; } = "Info";
    public string Category { get; init; } = "";
    public string Source { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>Shape of the JSON emitted by AntiForensicHunter.ps1 -JsonOut.</summary>
internal sealed class AfhRecord
{
    [JsonPropertyName("TimestampIso")] public string? TimestampIso { get; set; }
    [JsonPropertyName("Severity")] public string? Severity { get; set; }
    [JsonPropertyName("Category")] public string? Category { get; set; }
    [JsonPropertyName("Source")] public string? Source { get; set; }
    [JsonPropertyName("Detail")] public string? Detail { get; set; }
}

public sealed partial class AntiForensicsViewModel : ObservableObject
{
    private const string ScriptResource = "ForensicKit.Scripts.AntiForensicHunter.ps1";
    private const string ScriptName = "AntiForensicHunter";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] SeverityOrder = { "Alto", "Sospetto", "Info" };

    private readonly IScriptService _scripts;
    private readonly IAuditLogService _audit;
    private readonly IDialogService _dialog;

    private readonly List<TimelineEntry> _all = new();

    public AntiForensicsViewModel(IScriptService scripts, IAuditLogService audit, IDialogService dialog)
    {
        _scripts = scripts;
        _audit = audit;
        _dialog = dialog;
    }

    public ObservableCollection<TimelineEntry> Events { get; } = new();
    public ObservableCollection<string> SeverityFilters { get; } = new() { "Tutti", "Alto", "Sospetto", "Info" };

    [ObservableProperty] private string _selectedSeverity = "Tutti";
    [ObservableProperty] private bool _runElevated = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _statusMessage = "Premi \"Esegui analisi\" per costruire la timeline.";
    [ObservableProperty] private int _highCount;
    [ObservableProperty] private int _suspectCount;
    [ObservableProperty] private int _infoCount;

    partial void OnSelectedSeverityChanged(string value) => ApplyFilter();

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        StatusMessage = RunElevated
            ? "Analisi in corso (richiesta elevazione UAC)…"
            : "Analisi in corso…";
        Events.Clear();
        _all.Clear();

        var jsonOut = Path.Combine(Path.GetTempPath(), $"forensickit-afh-{Guid.NewGuid():N}.json");

        try
        {
            var content = EmbeddedResources.GetScriptContent(ScriptResource);
            var result = await _scripts.RunScriptToJsonAsync(content, ScriptName, RunElevated, jsonOut);

            if (!File.Exists(jsonOut))
            {
                StatusMessage = RunElevated
                    ? "Nessun risultato: elevazione annullata o analisi fallita."
                    : $"Nessun risultato prodotto. {result.Message}";
                return;
            }

            var json = await File.ReadAllTextAsync(jsonOut);
            var records = JsonSerializer.Deserialize<List<AfhRecord>>(json, JsonOptions) ?? new();

            foreach (var r in records)
            {
                DateTime? ts = null;
                if (DateTime.TryParse(r.TimestampIso, out var parsed))
                    ts = parsed;

                _all.Add(new TimelineEntry
                {
                    Timestamp = ts,
                    Severity = r.Severity ?? "Info",
                    Category = r.Category ?? "",
                    Source = r.Source ?? "",
                    Detail = r.Detail ?? ""
                });
            }

            HighCount = _all.Count(e => e.Severity.Equals("Alto", StringComparison.OrdinalIgnoreCase));
            SuspectCount = _all.Count(e => e.Severity.Equals("Sospetto", StringComparison.OrdinalIgnoreCase));
            InfoCount = _all.Count(e => !e.Severity.Equals("Alto", StringComparison.OrdinalIgnoreCase)
                                     && !e.Severity.Equals("Sospetto", StringComparison.OrdinalIgnoreCase));

            ApplyFilter();
            StatusMessage = $"{_all.Count} indicatori raccolti.";

            await _audit.AppendAsync(new ExecutionLogEntry
            {
                Category = "script",
                ToolId = "scriptz-timeline",
                ToolName = "Anti-Forensic Timeline",
                Action = RunElevated ? "run (elevated)" : "run",
                Result = ExecutionResult.Succeeded,
                Details = $"{_all.Count} indicatori; alto={HighCount} sospetto={SuspectCount}"
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "Errore durante l'analisi.";
            _dialog.ShowError("Timeline anti-forense", ex.Message);
        }
        finally
        {
            try { if (File.Exists(jsonOut)) File.Delete(jsonOut); } catch { /* best effort */ }
            IsRunning = false;
        }
    }

    private void ApplyFilter()
    {
        Events.Clear();

        IEnumerable<TimelineEntry> query = _all;
        if (SelectedSeverity != "Tutti")
            query = query.Where(e => e.Severity.Equals(SelectedSeverity, StringComparison.OrdinalIgnoreCase));

        // Ordine timeline: piu' recenti in alto, e a parita' di tempo per severita'.
        foreach (var e in query
                     .OrderByDescending(e => e.Timestamp ?? DateTime.MinValue)
                     .ThenBy(e => Array.IndexOf(SeverityOrder, e.Severity)))
        {
            Events.Add(e);
        }
    }
}
