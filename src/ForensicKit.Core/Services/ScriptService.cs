using System.Diagnostics;
using System.Text;

namespace ForensicKit.Core.Services;

public interface IScriptService
{
    /// <summary>
    /// Runs PowerShell script <paramref name="scriptContent"/> by materializing it to a
    /// temporary .ps1 and invoking powershell.exe. Output lines are streamed via
    /// <paramref name="outputLine"/> and also returned aggregated.
    /// </summary>
    Task<ProcessRunResult> RunScriptContentAsync(
        string scriptContent,
        string scriptDisplayName,
        bool elevate,
        IProgress<string>? outputLine = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a script passing "-JsonOut &lt;path&gt;" so it writes structured JSON to
    /// <paramref name="jsonOutPath"/>. Works with elevation (hidden window) because the
    /// result is read from the file, not from stdout. The caller reads the JSON file.
    /// </summary>
    Task<ProcessRunResult> RunScriptToJsonAsync(
        string scriptContent,
        string scriptDisplayName,
        bool elevate,
        string jsonOutPath,
        CancellationToken ct = default);
}

/// <summary>
/// Executes embedded PowerShell scripts via the Windows PowerShell host
/// (powershell.exe -ExecutionPolicy Bypass -File ...). The scripts ship inside the
/// application (never downloaded from remote) which keeps their contents auditable.
/// </summary>
public sealed class ScriptService : IScriptService
{
    private readonly IExecutionService _execution;

    public ScriptService(IExecutionService execution) => _execution = execution;

    /// <summary>
    /// Absolute path to Windows PowerShell. Using the full path avoids relying on the
    /// PATH environment variable (which is why a bare "powershell.exe" could fail).
    /// </summary>
    private static string PowerShellPath
    {
        get
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var full = Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(full) ? full : "powershell.exe";
        }
    }

    public async Task<ProcessRunResult> RunScriptContentAsync(
        string scriptContent,
        string scriptDisplayName,
        bool elevate,
        IProgress<string>? outputLine = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ForensicKit", "scripts");
        Directory.CreateDirectory(tempDir);

        var safeName = string.Concat(scriptDisplayName.Split(Path.GetInvalidFileNameChars()));
        var scriptPath = Path.Combine(tempDir, $"{safeName}.ps1");

        // Write with a UTF-8 BOM so PowerShell reads non-ASCII characters correctly.
        await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(true), ct)
            .ConfigureAwait(false);

        if (elevate)
        {
            // Elevated stdout cannot cross the UAC boundary, so run the script in a hidden
            // elevated PowerShell that writes ALL streams to a file, then read it back.
            var outFile = Path.Combine(tempDir, $"{safeName}.out.txt");
            try { File.Delete(outFile); } catch { /* ignore */ }

            var elevatedArgs =
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                $"\"& '{scriptPath}' *>&1 | Out-File -LiteralPath '{outFile}' -Encoding utf8\"";

            var elevatedResult = await _execution
                .LaunchElevatedAndWaitAsync(PowerShellPath, elevatedArgs, ct)
                .ConfigureAwait(false);

            var captured = new StringBuilder();
            if (File.Exists(outFile))
            {
                foreach (var line in await File.ReadAllLinesAsync(outFile, ct).ConfigureAwait(false))
                {
                    captured.AppendLine(line);
                    outputLine?.Report(line);
                }
            }

            try { File.Delete(scriptPath); } catch { /* best effort */ }
            try { File.Delete(outFile); } catch { /* best effort */ }

            return elevatedResult with { StandardOutput = captured.ToString() };
        }

        var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        try
        {
            return await _execution
                .RunAndCaptureAsync(PowerShellPath, args, tempDir, outputLine, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    public async Task<ProcessRunResult> RunScriptToJsonAsync(
        string scriptContent,
        string scriptDisplayName,
        bool elevate,
        string jsonOutPath,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ForensicKit", "scripts");
        Directory.CreateDirectory(tempDir);

        var safeName = string.Concat(scriptDisplayName.Split(Path.GetInvalidFileNameChars()));
        var scriptPath = Path.Combine(tempDir, $"{safeName}.ps1");
        await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(true), ct)
            .ConfigureAwait(false);

        var args = "-NoProfile -NonInteractive -ExecutionPolicy Bypass " +
                   $"-File \"{scriptPath}\" -JsonOut \"{jsonOutPath}\"";

        try
        {
            if (elevate)
                return await _execution.LaunchElevatedAndWaitAsync(PowerShellPath, args, ct)
                    .ConfigureAwait(false);

            return await _execution.RunAndCaptureAsync(PowerShellPath, args, tempDir, null, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }
}
