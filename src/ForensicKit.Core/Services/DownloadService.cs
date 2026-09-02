using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public interface IDownloadService
{
    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/> with progress
    /// reporting, HTTP-range resume of a partial file, and bounded retries.
    /// </summary>
    Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        int maxRetries = 3,
        CancellationToken ct = default);
}

/// <summary>
/// Asynchronous downloader supporting resume (via HTTP Range), progress reporting
/// and simple exponential-backoff retry on transient network failures.
/// </summary>
public sealed class DownloadService : IDownloadService
{
    private readonly IHttpClientFactoryLite _clientFactory;

    public DownloadService(IHttpClientFactoryLite clientFactory) => _clientFactory = clientFactory;

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partPath = destinationPath + ".part";

        Exception? lastError = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadOnceAsync(url, destinationPath, partPath, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastError = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1s, 2s, 4s...
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"Download failed after {maxRetries + 1} attempts: {lastError?.Message}", lastError);
    }

    private async Task DownloadOnceAsync(
        string url,
        string destinationPath,
        string partPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using var client = _clientFactory.Create();

        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        // If the server ignores the Range header, start over from scratch.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        if (!resuming && existing > 0)
        {
            File.Delete(partPath);
            existing = 0;
        }

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        if (total.HasValue && resuming)
            total += existing;

        var fileMode = resuming ? FileMode.Append : FileMode.Create;
        await using (var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fileStream = new FileStream(
                         partPath, fileMode, FileAccess.Write, FileShare.None,
                         bufferSize: 1024 * 128, useAsync: true))
        {
            var buffer = new byte[1024 * 128];
            long received = existing;
            var sw = Stopwatch.StartNew();
            long lastReported = existing;
            var lastReportTime = TimeSpan.Zero;

            int read;
            while ((read = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;

                var elapsed = sw.Elapsed - lastReportTime;
                if (elapsed.TotalMilliseconds >= 200)
                {
                    var speed = (received - lastReported) / Math.Max(elapsed.TotalSeconds, 0.001);
                    progress?.Report(new DownloadProgress(received, total, speed));
                    lastReported = received;
                    lastReportTime = sw.Elapsed;
                }
            }

            progress?.Report(new DownloadProgress(received, total, 0));
        }

        // Atomic-ish finalize: move the completed .part onto the destination.
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);
        File.Move(partPath, destinationPath);
    }
}
