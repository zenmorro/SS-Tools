using System.Text;
using System.Text.Json;
using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public interface IAuditLogService
{
    Task AppendAsync(ExecutionLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionLogEntry>> ReadAllAsync(CancellationToken ct = default);
    string LogFilePath { get; }
}

/// <summary>
/// Append-only (write-once) audit log stored as JSON Lines. Each execution/download
/// appends one immutable line. The service never rewrites or deletes prior lines,
/// which preserves a simple chain-of-custody trail for forensic use.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false // one entry per line
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AuditLogService(AppPaths paths) => _paths = paths;

    public string LogFilePath => _paths.AuditLogFile;

    public async Task AppendAsync(ExecutionLogEntry entry, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // FileMode.Append guarantees existing content is never overwritten.
            await using var stream = new FileStream(
                _paths.AuditLogFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(line);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<ExecutionLogEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        var list = new List<ExecutionLogEntry>();
        if (!File.Exists(_paths.AuditLogFile))
            return list;

        foreach (var line in await File.ReadAllLinesAsync(_paths.AuditLogFile, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var entry = JsonSerializer.Deserialize<ExecutionLogEntry>(line, JsonOptions);
                if (entry is not null)
                    list.Add(entry);
            }
            catch
            {
                // Skip malformed lines rather than failing the whole read.
            }
        }

        return list;
    }
}
