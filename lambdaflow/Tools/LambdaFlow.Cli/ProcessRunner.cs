using System.Diagnostics;

namespace LambdaFlow.Cli;

internal static class ProcessRunner
{
    internal static async Task RunShellCommand(string command, string workingDirectory) {
        Console.WriteLine($"> {command}");

        var startInfo = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start command: {command}");

        var stdoutTask = RelayAsync(process.StandardOutput, Console.Out);
        var stderrTask = RelayAsync(process.StandardError, Console.Error);

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {command}");
    }

    internal static async Task RunDotnet(params string[] args) {
        Console.WriteLine($"> dotnet {string.Join(" ", args)}");

        var startInfo = new ProcessStartInfo {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");

        var stdoutTask = RelayAsync(process.StandardOutput, Console.Out);
        var stderrTask = RelayAsync(process.StandardError, Console.Error);

        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet failed with exit code {process.ExitCode}.");
    }

    private static async Task RelayAsync(TextReader reader, TextWriter writer) {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
            writer.WriteLine(line);
    }
}
