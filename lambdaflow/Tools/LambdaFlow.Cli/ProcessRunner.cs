using System.Diagnostics;
using System.Text;

namespace LambdaFlow.Cli;

internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    internal static async Task<ProcessRunResult> RunShellCommand(
        string command,
        string workingDirectory,
        int? timeoutSeconds = null,
        bool throwOnFailure = true) {
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

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTask = RelayAsync(process.StandardOutput, Console.Out, stdout);
        var stderrTask = RelayAsync(process.StandardError, Console.Error, stderr);

        try {
            if (timeoutSeconds is > 0) {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds.Value));
                await process.WaitForExitAsync(timeout.Token);
            }
            else {
                await process.WaitForExitAsync();
            }
        }
        catch (OperationCanceledException) {
            try { process.Kill(entireProcessTree: true); } catch { }
            await Task.WhenAll(stdoutTask, stderrTask);
            throw new TimeoutException($"Command timed out after {timeoutSeconds} seconds: {command}");
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        var result = new ProcessRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());

        if (throwOnFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {command}");

        return result;
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

    private static async Task RelayAsync(TextReader reader, TextWriter writer, StringBuilder? capture = null) {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null) {
            capture?.AppendLine(line);
            writer.WriteLine(line);
        }
    }
}
