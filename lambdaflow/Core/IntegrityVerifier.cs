using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace lambdaflow.lambdaflow.Core
{
    internal sealed class IntegrityManifest
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; } = "SHA-256";

        [JsonPropertyName("files")]
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();
    }

    internal static class IntegrityVerifier
    {
        internal static void VerifyApplicationBundle() {
            if (Config.SecurityMode != SecurityMode.Hardened)
                throw new InvalidOperationException("LambdaFlow only supports the hardened security mode.");

            var baseDir      = EnsureTrailingSeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var manifestPath = Path.Combine(baseDir, Config.IntegrityManifestFile);

            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"Integrity manifest not found at '{manifestPath}'. Build the app with LambdaFlow CLI before running it.");

            var manifest = JsonSerializer.Deserialize<IntegrityManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidOperationException("Integrity manifest is malformed.");

            if (manifest.Version != 1)
                throw new InvalidOperationException($"Unsupported integrity manifest version '{manifest.Version}'.");

            if (!string.Equals(manifest.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported integrity algorithm '{manifest.Algorithm}'.");

            if (manifest.Files.Count == 0)
                throw new InvalidOperationException("Integrity manifest does not contain any files.");

            foreach (var item in manifest.Files) {
                var relativePath = NormalizeManifestPath(item.Key);
                var expectedHash = item.Value;
                var fullPath     = Path.GetFullPath(Path.Combine(baseDir, relativePath));

                if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Integrity manifest path escapes the app directory: '{item.Key}'.");

                if (!File.Exists(fullPath))
                    throw new FileNotFoundException($"Required app file not found: '{relativePath}'.");

                var actualHash = ComputeSha256(fullPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Integrity check failed for '{relativePath}'.");
            }
        }

        private static string NormalizeManifestPath(string path) {
            if (Path.IsPathRooted(path))
                throw new InvalidOperationException($"Integrity manifest path must be relative: '{path}'.");

            var normalized = path.Replace('\\', '/');
            foreach (var segment in normalized.Split('/')) {
                if (segment == "..")
                    throw new InvalidOperationException($"Integrity manifest path escapes the app directory: '{path}'.");
            }

            return normalized;
        }

        private static string EnsureTrailingSeparator(string path) {
            if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        private static string ComputeSha256(string path) {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
