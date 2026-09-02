using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.App.Services;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;
using Microsoft.Win32;

namespace ForensicKit.App.ViewModels;

public sealed partial class ScriptsViewModel : ObservableObject
{
    private readonly IScriptService _scripts;
    private readonly IAuditLogService _audit;
    private readonly IDialogService _dialog;

    private CancellationTokenSource? _cts;
    private readonly StringBuilder _buffer = new();

    public ScriptsViewModel(IScriptService scripts, IAuditLogService audit, IDialogService dialog)
    {
        _scripts = scripts;
        _audit = audit;
        _dialog = dialog;

        foreach (var s in EmbeddedResources.GetScriptCatalog())
            Scripts.Add(s);
        SelectedScript = Scripts.FirstOrDefault();
    }

    public ObservableCollection<ScriptDefinition> Scripts { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private ScriptDefinition? _selectedScript;

    [ObservableProperty] private string _consoleOutput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private bool _runElevated;

    private void Append(string line)
    {
        _buffer.AppendLine(line);
        // Marshal to UI thread; Progress<T> already captures the sync context,
        // but this method is also called directly in a couple of places.
        Application.Current.Dispatcher.Invoke(() => ConsoleOutput = _buffer.ToString());
    }

    private bool CanRun() => !IsRunning && SelectedScript is not null;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (SelectedScript is null) return;
        var script = SelectedScript;

        _buffer.Clear();
        ConsoleOutput = "";
        IsRunning = true;
        _cts = new CancellationTokenSource();

        var elevate = RunElevated || script.RequiresElevation;
        Append($"[ForensicKit] Avvio script '{script.Name}'{(elevate ? " (elevato)" : "")}…");
        Append("");

        var progress = new Progress<string>(Append);

        try
        {
            var content = EmbeddedResources.GetScriptContent(script.ResourceName);
            var result = await _scripts.RunScriptContentAsync(
                content, script.Id, elevate, progress, _cts.Token);

            Append("");
            if (elevate && string.IsNullOrWhiteSpace(result.StandardOutput))
                Append("[ForensicKit] Nessun output prodotto (elevazione annullata o script senza risultati). "
                     + result.Message);
            else
                Append($"[ForensicKit] {result.Message}");

            await _audit.AppendAsync(new ExecutionLogEntry
            {
                Category = "script",
                ToolId = script.Id,
                ToolName = script.Name,
                Action = elevate ? "run (elevated)" : "run",
                Result = result.Started ? ExecutionResult.Succeeded : ExecutionResult.Failed,
                Details = result.Message
            });
        }
        catch (OperationCanceledException)
        {
            Append("");
            Append("[ForensicKit] Esecuzione annullata.");
        }
        catch (Exception ex)
        {
            Append("");
            Append($"[ForensicKit] Errore: {ex.Message}");
            _dialog.ShowError("Errore script", ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ExportTxt()
    {
        if (string.IsNullOrEmpty(ConsoleOutput)) return;
        var dlg = new SaveFileDialog
        {
            Filter = "File di testo (*.txt)|*.txt",
            FileName = $"{SelectedScript?.Id ?? "output"}.txt"
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllText(dlg.FileName, ConsoleOutput, new UTF8Encoding(true));
    }

    [RelayCommand]
    private void ExportJson()
    {
        if (string.IsNullOrEmpty(ConsoleOutput)) return;
        var dlg = new SaveFileDialog
        {
            Filter = "File JSON (*.json)|*.json",
            FileName = $"{SelectedScript?.Id ?? "output"}.json"
        };
        if (dlg.ShowDialog() != true) return;

        var payload = new
        {
            script = SelectedScript?.Name,
            scriptId = SelectedScript?.Id,
            generatedUtc = DateTime.UtcNow,
            machine = Environment.MachineName,
            user = Environment.UserName,
            lines = ConsoleOutput.Replace("\r\n", "\n").Split('\n')
        };
        File.WriteAllText(dlg.FileName,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(true));
    }
}
