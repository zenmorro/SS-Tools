using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;

namespace ForensicKit.App.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly IAuditLogService _audit;

    public LogsViewModel(IAuditLogService audit) => _audit = audit;

    public ObservableCollection<ExecutionLogEntry> Entries { get; } = new();

    [ObservableProperty] private string _logPath = "";
    [ObservableProperty] private int _count;

    public async Task InitializeAsync()
    {
        LogPath = _audit.LogFilePath;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Entries.Clear();
        var all = await _audit.ReadAllAsync();
        // Newest first.
        foreach (var e in all.OrderByDescending(e => e.TimestampUtc))
            Entries.Add(e);
        Count = Entries.Count;
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        if (System.IO.File.Exists(LogPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LogPath}\"")
            {
                UseShellExecute = true
            });
    }
}
