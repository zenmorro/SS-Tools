using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.App.Services;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;

namespace ForensicKit.App.ViewModels;

/// <summary>
/// View model for a single tool card in the dashboard grid.
/// </summary>
public sealed partial class ToolCardViewModel : ObservableObject
{
    private readonly IToolInstallService _install;
    private readonly IExecutionService _execution;
    private readonly ISignatureService _signature;
    private readonly IAuditLogService _audit;
    private readonly IDialogService _dialog;
    private readonly ISettingsService _settings;

    public ToolEntry Tool { get; }

    public ToolCardViewModel(
        ToolEntry tool,
        IToolInstallService install,
        IExecutionService execution,
        ISignatureService signature,
        IAuditLogService audit,
        IDialogService dialog,
        ISettingsService settings)
    {
        Tool = tool;
        _install = install;
        _execution = execution;
        _signature = signature;
        _audit = audit;
        _dialog = dialog;
        _settings = settings;

        _isFavorite = settings.Current.Favorites.Contains(tool.Id);
        RefreshStatus();
    }

    public string Name => Tool.Name;
    public string Author => Tool.Author;
    public string Category => Tool.Category;
    public string Description => Tool.Description;
    public string Initials =>
        string.IsNullOrWhiteSpace(Tool.Name) ? "?" : Tool.Name[..1].ToUpperInvariant();

    [ObservableProperty] private ToolStatus _status;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    private double _progressPercent;

    [ObservableProperty] private string _statusDetail = string.Empty;

    public bool HasProgress => IsBusy && ProgressPercent > 0;

    public void RefreshStatus()
    {
        Status = _install.IsInstalled(Tool) ? ToolStatus.Downloaded : ToolStatus.NotDownloaded;
        DownloadCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }

    public async Task CheckUpdateAsync(CancellationToken ct = default)
    {
        if (Status == ToolStatus.NotDownloaded)
            return;
        var (updateAvailable, _) = await _install.CheckForUpdateAsync(Tool, ct);
        if (updateAvailable)
            Status = ToolStatus.UpdateAvailable;
    }

    private bool CanDownload() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        IsBusy = true;
        StatusDetail = "Download in corso…";
        ProgressPercent = 0;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Percentage is { } pct)
            {
                ProgressPercent = pct;
                StatusDetail = $"{pct:0}%  ({p.BytesPerSecond / 1024:0} KB/s)";
            }
            else
            {
                StatusDetail = $"{p.BytesReceived / 1024:0} KB scaricati…";
            }
        });

        try
        {
            var result = await _install.InstallAsync(Tool, progress);
            Status = ToolStatus.Downloaded;
            StatusDetail = $"Installato (SHA-256 {result.ArchiveSha256[..12]}…)";

            await _audit.AppendAsync(new ExecutionLogEntry
            {
                Category = "download",
                ToolId = Tool.Id,
                ToolName = Tool.Name,
                Action = "install",
                Result = ExecutionResult.Succeeded,
                Details = $"Installed to {result.InstallFolder}; version {result.ResolvedVersion}",
                Sha256 = result.ArchiveSha256
            });
        }
        catch (Exception ex)
        {
            StatusDetail = "Errore durante il download.";
            _dialog.ShowError($"Download di {Tool.Name} fallito", ex.Message);
            await _audit.AppendAsync(new ExecutionLogEntry
            {
                Category = "download",
                ToolId = Tool.Id,
                ToolName = Tool.Name,
                Action = "install",
                Result = ExecutionResult.Failed,
                Details = ex.Message
            });
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 0;
            RefreshStatus();
        }
    }

    private bool CanRun() => !IsBusy && Status != ToolStatus.NotDownloaded;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var exePath = _install.ResolveExecutablePath(Tool);
        if (exePath is null)
        {
            _dialog.ShowError(Tool.Name, "Eseguibile non trovato. Riprova a scaricare il tool.");
            RefreshStatus();
            return;
        }

        // Transparency: show source, hash and signature before the first launch.
        var mustAcknowledge = !(_settings.Current.RememberTrustAcknowledgement
            && _settings.Current.AcknowledgedTools.Contains(Tool.Id));

        if (mustAcknowledge)
        {
            var state = _install.ReadState(Tool);
            var sig = _signature.Inspect(exePath);
            if (!_dialog.ConfirmToolTrust(Tool, state, sig))
                return;

            if (_settings.Current.RememberTrustAcknowledgement &&
                !_settings.Current.AcknowledgedTools.Contains(Tool.Id))
            {
                _settings.Current.AcknowledgedTools.Add(Tool.Id);
                await _settings.SaveAsync();
            }
        }

        // Allow the analyst to tweak arguments before launch.
        var args = _dialog.PromptForArguments(Tool.Name, Tool.Args);
        if (args is null)
            return; // cancelled

        var result = _execution.Launch(exePath, args, Tool.RequiresElevation);

        await _audit.AppendAsync(new ExecutionLogEntry
        {
            Category = "execute",
            ToolId = Tool.Id,
            ToolName = Tool.Name,
            Action = $"launch{(Tool.RequiresElevation ? " (elevated)" : "")}",
            Result = result.Started ? ExecutionResult.Started : ExecutionResult.Failed,
            Details = $"{result.Message} args=\"{args}\"",
            Sha256 = _install.ReadState(Tool)?.ArchiveSha256
        });

        if (!result.Started)
            _dialog.ShowError($"Avvio di {Tool.Name} fallito", result.Message);
    }

    [RelayCommand]
    private void Info()
    {
        var state = _install.ReadState(Tool);
        var lines = new List<string>
        {
            $"Nome: {Tool.Name}",
            $"Autore: {Tool.Author}",
            $"Categoria: {Tool.Category}",
            "",
            Tool.Description,
            "",
            $"Fonte: {Tool.SourceType}",
            $"Homepage: {Tool.Homepage ?? "-"}",
            $"Eseguibile: {Tool.Executable}",
            $"Richiede elevazione: {(Tool.RequiresElevation ? "Sì" : "No")}"
        };
        if (state is not null)
        {
            lines.Add("");
            lines.Add($"Versione installata: {state.InstalledVersion ?? "-"}");
            lines.Add($"SHA-256: {state.ArchiveSha256}");
            lines.Add($"Installato il: {state.InstalledUtc:u}");
        }

        _dialog.ShowInfo($"Info — {Tool.Name}", string.Join(Environment.NewLine, lines));
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        IsFavorite = !IsFavorite;
        var favs = _settings.Current.Favorites;
        if (IsFavorite && !favs.Contains(Tool.Id))
            favs.Add(Tool.Id);
        else if (!IsFavorite)
            favs.Remove(Tool.Id);
        await _settings.SaveAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        DownloadCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasProgress));
    }
}
