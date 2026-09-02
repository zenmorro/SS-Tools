using System.Diagnostics;
using System.Text;

namespace ForensicKit.Core.Services;

/// <summary>Outcome of launching/running a process.</summary>
public sealed record ProcessRunResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string Message);

public interface IExecutionService
{
    /// <summary>
    /// Launches a GUI tool without waiting for it to exit. When <paramref name="elevate"/>
    /// is true, uses the ShellExecute "runas" verb to trigger a UAC prompt.
    /// </summary>
    ProcessRunResult Launch(string executablePath, string arguments, bool elevate);

    /// <summary>
    /// Runs a console tool to completion, capturing stdout/stderr. Elevation is not
    /// supported here because captured redirection requires ShellExecute=false.
    /// </summary>
    Task<ProcessRunResult> RunAndCaptureAsync(
        string executablePath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? outputLine = null,
        CancellationToken ct = default);

    /// <summary>
    /// Launches an elevated (UAC) process with a hidden window and waits for it to exit.
    /// stdout cannot be redirected across the elevation boundary, so callers that need
    /// output should have the elevated process write to a file and read it afterwards.
    /// </summary>
    Task<ProcessRunResult> LaunchElevatedAndWaitAsync(
        string executablePath,
        string arguments,
        CancellationToken ct = default);
}

public sealed class ExecutionService : IExecutionService
{
    public ProcessRunResult Launch(string executablePath, string arguments, bool elevate)
    {
        if (!File.Exists(executablePath))
            return new ProcessRunResult(false, null, "", "", $"Executable not found: {executablePath}");

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = true // required for the "runas" verb
        };

        if (elevate)
            psi.Verb = "runas";

        try
        {
            var process = Process.Start(psi);
            return new ProcessRunResult(
                process is not null, null, "", "",
                process is not null
                    ? $"Launched (PID {process.Id})."
                    : "Process did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt.
            return new ProcessRunResult(false, null, "", "", "Elevation was cancelled by the user (UAC).");
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(false, null, "", "", $"Failed to launch: {ex.Message}");
        }
    }

    public async Task<ProcessRunResult> LaunchElevatedAndWaitAsync(
        string executablePath,
        string arguments,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new ProcessRunResult(false, null, "", "", "Process did not start.");

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return new ProcessRunResult(true, process.ExitCode, "", "",
                $"Exited with code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new ProcessRunResult(false, null, "", "", "Elevation was cancelled by the user (UAC).");
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(false, null, "", "", $"Failed to launch: {ex.Message}");
        }
    }

    public async Task<ProcessRunResult> RunAndCaptureAsync(
        string executablePath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? outputLine = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
            return new ProcessRunResult(false, null, "", "", $"Executable not found: {executablePath}");

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            outputLine?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            outputLine?.Report(e.Data);
        };

        try
        {
            if (!process.Start())
                return new ProcessRunResult(false, null, "", "", "Process did not start.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return new ProcessRunResult(
                true, process.ExitCode, stdout.ToString(), stderr.ToString(),
                $"Exited with code {process.ExitCode}.");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return new ProcessRunResult(true, null, stdout.ToString(), stderr.ToString(), "Cancelled.");
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(false, null, stdout.ToString(), stderr.ToString(),
                $"Failed: {ex.Message}");
        }
    }
}
