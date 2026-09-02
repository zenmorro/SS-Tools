using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForensicKit.Core.Models;

/// <summary>
/// Reads/writes <see cref="SourceType"/> as the snake_case strings used in the manifest
/// ("direct_url", "github_release") while also tolerating the PascalCase names.
/// </summary>
public sealed class SourceTypeJsonConverter : JsonConverter<SourceType>
{
    public override SourceType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()?.Trim().Replace("_", "").Replace("-", "");
        return s?.ToLowerInvariant() switch
        {
            "directurl" => SourceType.DirectUrl,
            "githubrelease" => SourceType.GithubRelease,
            _ => throw new JsonException($"Unknown sourceType '{reader.GetString()}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, SourceType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value == SourceType.GithubRelease ? "github_release" : "direct_url");
}

/// <summary>
/// Reads/writes <see cref="PackageType"/> as "zip"/"exe" (case-insensitive).
/// </summary>
public sealed class PackageTypeJsonConverter : JsonConverter<PackageType>
{
    public override PackageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString()?.Trim().ToLowerInvariant() switch
        {
            "zip" => PackageType.Zip,
            "exe" => PackageType.Exe,
            _ => throw new JsonException($"Unknown packageType '{reader.GetString()}'.")
        };

    public override void Write(Utf8JsonWriter writer, PackageType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value == PackageType.Zip ? "zip" : "exe");
}
