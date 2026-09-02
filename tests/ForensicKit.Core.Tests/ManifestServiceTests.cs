using ForensicKit.Core.Infrastructure;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;
using Xunit;

namespace ForensicKit.Core.Tests;

public class ManifestServiceTests
{
    private static ManifestService CreateService()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "ForensicKitTests", Guid.NewGuid().ToString("N")));
        var settings = new SettingsService(paths);
        // Fake factory not needed for Validate() tests.
        return new ManifestService(paths, settings, new FakeHttpClientFactory(), () => "{}");
    }

    [Fact]
    public void Validate_NullManifest_ReportsProblem()
    {
        var svc = CreateService();
        var problems = svc.Validate(null);
        Assert.NotEmpty(problems);
    }

    [Fact]
    public void Validate_ValidManifest_ReturnsNoProblems()
    {
        var svc = CreateService();
        var manifest = new ToolManifest
        {
            SchemaVersion = 1,
            Tools =
            {
                new ToolEntry
                {
                    Id = "everything",
                    Name = "Everything",
                    SourceType = SourceType.DirectUrl,
                    DownloadUrl = "https://example.com/e.zip",
                    PackageType = PackageType.Zip,
                    Executable = "Everything.exe"
                }
            }
        };

        Assert.Empty(svc.Validate(manifest));
    }

    [Fact]
    public void Validate_DirectUrlWithoutUrl_ReportsProblem()
    {
        var svc = CreateService();
        var manifest = new ToolManifest
        {
            Tools = { new ToolEntry { Id = "x", Name = "X", SourceType = SourceType.DirectUrl, Executable = "x.exe" } }
        };

        Assert.Contains(svc.Validate(manifest), p => p.Contains("downloadUrl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DuplicateIds_ReportsProblem()
    {
        var svc = CreateService();
        var manifest = new ToolManifest
        {
            Tools =
            {
                new ToolEntry { Id = "dup", Name = "A", SourceType = SourceType.GithubRelease, Owner = "o", Repo = "r", PackageType = PackageType.Exe },
                new ToolEntry { Id = "dup", Name = "B", SourceType = SourceType.GithubRelease, Owner = "o", Repo = "r", PackageType = PackageType.Exe }
            }
        };

        Assert.Contains(svc.Validate(manifest), p => p.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deserialize_SnakeCaseEnums_AreParsed()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "tools": [
            { "id": "e", "name": "E", "sourceType": "direct_url", "downloadUrl": "https://x/e.zip",
              "packageType": "zip", "executable": "e.exe" },
            { "id": "j", "name": "J", "sourceType": "github_release", "owner": "o", "repo": "r",
              "packageType": "exe", "executable": "j.exe" }
          ]
        }
        """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<ToolManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Tools.Count);
        Assert.Equal(SourceType.DirectUrl, manifest.Tools[0].SourceType);
        Assert.Equal(PackageType.Zip, manifest.Tools[0].PackageType);
        Assert.Equal(SourceType.GithubRelease, manifest.Tools[1].SourceType);
        Assert.Equal(PackageType.Exe, manifest.Tools[1].PackageType);
    }

    [Fact]
    public void ShippedManifest_ParsesAndValidates_WithFiveTools()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools.json");
        Assert.True(File.Exists(path), $"Shipped manifest not found at {path}");

        var json = File.ReadAllText(path);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ToolManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal(5, manifest!.Tools.Count);

        var svc = CreateService();
        Assert.Empty(svc.Validate(manifest));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactoryLite
    {
        public HttpClient Create() => new();
    }
}
