using System.Security.Cryptography;

namespace ForensicKit.Core.Services;

public interface IHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default);
    bool Matches(string computedHex, string? expectedHex);
}

/// <summary>
/// SHA-256 hashing used for download integrity verification and the audit log.
/// </summary>
public sealed class HashService : IHashService
{
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 128, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool Matches(string computedHex, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
            return true; // nothing to compare against -> treated as "not enforced"

        return string.Equals(
            computedHex.Replace(" ", string.Empty).Trim(),
            expectedHex.Replace(" ", string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
