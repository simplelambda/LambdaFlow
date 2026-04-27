using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LambdaFlow.Cli;

internal sealed class IntegrityManifest
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "SHA-256";

    [JsonPropertyName("files")]
    public SortedDictionary<string, string> Files { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
}

internal static class IntegrityManifestWriter
{
    internal static void Write(string appDir) {
        var manifest = new IntegrityManifest();

        foreach (var file in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories)) {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "lambdaflow.integrity.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(appDir, file).Replace('\\', '/');
            manifest.Files[relativePath] = ComputeSha256(file);
        }

        var options = new JsonSerializerOptions {
            WriteIndented = true
        };

        File.WriteAllText(Path.Combine(appDir, "lambdaflow.integrity.json"), JsonSerializer.Serialize(manifest, options));
    }

    private static string ComputeSha256(string path) {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
