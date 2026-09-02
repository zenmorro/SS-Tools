using System.IO.Compression;
using ForensicKit.Core.Services;
using Xunit;

namespace ForensicKit.Core.Tests;

public class HashServiceTests
{
    [Fact]
    public async Task ComputeSha256_KnownContent_MatchesExpected()
    {
        var svc = new HashService();
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "abc");
            var hash = await svc.ComputeSha256Async(file);
            // SHA-256("abc")
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("ABC123", "abc123", true)]   // case-insensitive match
    [InlineData("abc", "def", false)]
    [InlineData("abc", "", true)]            // empty expected => not enforced
    [InlineData("abc", null, true)]
    public void Matches_BehavesAsExpected(string computed, string? expected, bool result)
    {
        var svc = new HashService();
        Assert.Equal(result, svc.Matches(computed, expected));
    }
}

public class ExtractionServiceTests
{
    [Fact]
    public async Task ExtractZip_NormalEntries_AreExtracted()
    {
        var svc = new ExtractionService();
        var dir = Path.Combine(Path.GetTempPath(), "FK_Extract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "test.zip");
        var outDir = Path.Combine(dir, "out");

        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("tool.exe");
                await using var s = entry.Open();
                await s.WriteAsync("hello"u8.ToArray());
            }

            var files = await svc.ExtractZipAsync(zipPath, outDir);
            Assert.Single(files);
            Assert.True(File.Exists(Path.Combine(outDir, "tool.exe")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExtractZip_ZipSlipEntry_IsBlocked()
    {
        var svc = new ExtractionService();
        var dir = Path.Combine(Path.GetTempPath(), "FK_Slip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "evil.zip");
        var outDir = Path.Combine(dir, "out");

        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // Traversal entry attempting to escape the destination folder.
                var entry = zip.CreateEntry("..\\..\\evil.txt");
                await using var s = entry.Open();
                await s.WriteAsync("pwned"u8.ToArray());
            }

            await Assert.ThrowsAsync<IOException>(() => svc.ExtractZipAsync(zipPath, outDir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
