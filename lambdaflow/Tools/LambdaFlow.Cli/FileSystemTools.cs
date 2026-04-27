using System.IO.Compression;

namespace LambdaFlow.Cli;

internal static class FileSystemTools
{
    internal static void CopyDirectory(string sourceDir, string targetDir) {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Directory not found: '{sourceDir}'.");

        Directory.CreateDirectory(targetDir);

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourceDir, sourcePath);
            var targetPath   = Path.Combine(targetDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    internal static void CreatePak(string sourceDir, string targetPak) {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Frontend directory not found: '{sourceDir}'.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPak)!);
        if (File.Exists(targetPak))
            File.Delete(targetPak);

        ZipFile.CreateFromDirectory(sourceDir, targetPak, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    internal static void WriteFile(string path, string contents) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
