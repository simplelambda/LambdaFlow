using System.Runtime.InteropServices;

namespace LambdaFlow.Cli;

internal sealed record BuildTarget(
    string PlatformKey,
    string ArchKey,
    string RuntimeIdentifier,
    string OutputFolder,
    string HostProjectFile,
    string HostBinaryName,
    string ExecutableExtension)
{
    internal static BuildTarget Resolve(string? value) {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? CurrentTargetName()
            : value.Trim().ToLowerInvariant();

        return normalized switch {
            "windows-x64" or "win-x64" => Windows("x64"),
            "windows-arm64" or "win-arm64" => Windows("arm64"),
            "linux-x64" => Linux("x64"),
            "linux-arm64" => Linux("arm64"),
            _ => throw new ArgumentException(
                $"Unsupported target '{value}'. Allowed values: windows-x64, windows-arm64, linux-x64, linux-arm64.")
        };
    }

    private static BuildTarget Windows(string arch) {
        return new BuildTarget(
            "windows",
            arch,
            $"win-{arch}",
            $"windows-{arch}",
            "lambdaflow.windows.csproj",
            "lambdaflow.windows.exe",
            ".exe");
    }

    private static BuildTarget Linux(string arch) {
        return new BuildTarget(
            "linux",
            arch,
            $"linux-{arch}",
            $"linux-{arch}",
            "lambdaflow.linux.csproj",
            "lambdaflow.linux",
            "");
    }

    private static string CurrentTargetName() {
        var platform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : throw new PlatformNotSupportedException(
                    "LambdaFlow CLI currently builds Windows and Linux targets.");

        var arch = RuntimeInformation.ProcessArchitecture switch {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"Architecture '{RuntimeInformation.ProcessArchitecture}' is not supported.")
        };

        return $"{platform}-{arch}";
    }
}
