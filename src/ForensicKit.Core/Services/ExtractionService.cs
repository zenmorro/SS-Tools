using System.IO.Compression;

namespace ForensicKit.Core.Services;

public interface IExtractionService
{
    /// <summary>
    /// Extracts a ZIP archive into <paramref name="destinationFolder"/>, guarding against
    /// path-traversal ("zip slip") entries. Returns the list of extracted file paths.
    /// </summary>
    Task<IReadOnlyList<string>> ExtractZipAsync(
        string zipPath, string destinationFolder, CancellationToken ct = default);
}

public sealed class ExtractionService : IExtractionService
{
    public Task<IReadOnlyList<string>> ExtractZipAsync(
        string zipPath, string destinationFolder, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            var fullDest = Path.GetFullPath(destinationFolder);
            var extracted = new List<string>();

            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                // Directory entry.
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(fullDest, entry.FullName));

                // Zip-slip protection: refuse to write outside the destination folder.
                if (!targetPath.StartsWith(fullDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !targetPath.Equals(fullDest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        $"Blocked unsafe zip entry '{entry.FullName}' that escapes the destination folder.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
                extracted.Add(targetPath);
            }

            return extracted;
        }, ct);
    }
}
