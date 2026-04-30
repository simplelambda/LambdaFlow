using System.Text.Json;
using System.Text.Json.Nodes;

namespace LambdaFlow.Cli;

internal static class BuildCommand
{
    internal static async Task<int> Run(string[] args) {
        var options       = new CommandOptions(args);
        var projectDir    = Path.GetFullPath(options.Positionals.Count > 0 ? options.Positionals[0] : Directory.GetCurrentDirectory());
        var frameworkRoot = ProjectPaths.ResolveFrameworkRoot(options, projectDir);
        var forceDebug    = options.HasFlag("--debug");
        var config        = Program.ReadConfig(projectDir);
        var windowsX64    = config.GetWindowsX64();

        var backendSourceDir = Path.Combine(projectDir, config.DevelopmentBackendFolder);
        var frontendDir      = Path.Combine(projectDir, config.DevelopmentFrontendFolder);
        var resultRoot       = Path.Combine(projectDir, config.ResultFolder);
        var appDir           = Path.Combine(resultRoot, $"{Sanitize(config.AppName)}-{Sanitize(config.AppVersion)}", "windows-x64");

        await RunPreBuildCommands(config, projectDir);

        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);

        Directory.CreateDirectory(appDir);

        if (!string.IsNullOrWhiteSpace(windowsX64.CompileCommand))
            await ProcessRunner.RunShellCommand(windowsX64.CompileCommand, backendSourceDir);
        else
            Console.WriteLine("No backend compile command configured; copying backend source folder.");

        var backendOutputDir = Path.GetFullPath(Path.Combine(backendSourceDir, windowsX64.CompileDirectory));
        var appBackendDir    = Path.Combine(appDir, "backend");
        FileSystemTools.CopyDirectory(backendOutputDir, appBackendDir);

        await ProcessRunner.RunDotnet(
            "publish",
            ProjectPaths.HostProject(frameworkRoot),
            "-c",
            "Release",
            "-r",
            "win-x64",
            "--self-contained",
            "true",
            "-o",
            appDir);

        var defaultExe = Path.Combine(appDir, "lambdaflow.windows.exe");
        var targetExe  = Path.Combine(appDir, $"{Sanitize(config.AppName)}.exe");
        if (File.Exists(defaultExe) && !string.Equals(defaultExe, targetExe, StringComparison.OrdinalIgnoreCase))
            File.Move(defaultExe, targetExe, overwrite: true);

        WriteAppConfig(projectDir, appDir, forceDebug);
        FileSystemTools.CreatePak(frontendDir, Path.Combine(appDir, "frontend.pak"));
        IntegrityManifestWriter.Write(appDir);

        Console.WriteLine($"LambdaFlow app built at: {appDir}");
        return 0;
    }

    private static void WriteAppConfig(string projectDir, string appDir, bool forceDebug) {
        var sourcePath = Path.Combine(projectDir, "config.json");
        var targetPath = Path.Combine(appDir, "config.json");

        if (!forceDebug) {
            File.Copy(sourcePath, targetPath, overwrite: true);
            return;
        }

        var documentOptions = new JsonDocumentOptions {
            CommentHandling     = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var node = JsonNode.Parse(File.ReadAllText(sourcePath), documentOptions: documentOptions)
            ?? throw new InvalidOperationException("config.json is malformed.");

        if (node is not JsonObject root)
            throw new InvalidOperationException("config.json root must be an object.");

        root["debug"] = new JsonObject {
            ["enabled"]                     = true,
            ["frontendDevTools"]            = true,
            ["openFrontendDevToolsOnStart"] = true,
            ["captureFrontendConsole"]      = true,
            ["showBackendConsole"]          = true,
            ["backendLogLevel"]             = "debug"
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(targetPath, node.ToJsonString(options) + Environment.NewLine);
    }

    private static async Task RunPreBuildCommands(LambdaFlowConfig config, string projectDir) {
        foreach (var command in config.Build.PreBuild.Where(c => c.Enabled)) {
            var displayName = string.IsNullOrWhiteSpace(command.Name)
                ? command.Command
                : command.Name;

            if (string.IsNullOrWhiteSpace(command.Command)) {
                var message = $"Pre-build command failed: {displayName}{Environment.NewLine}"
                            + "Command is empty.";

                if (command.ContinueOnError) {
                    Console.Error.WriteLine(message);
                    continue;
                }

                throw new InvalidOperationException(message);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? projectDir
                : command.WorkingDirectory;

            var effectiveWorkingDirectory = Path.IsPathRooted(workingDirectory)
                ? workingDirectory
                : Path.GetFullPath(Path.Combine(projectDir, workingDirectory));

            if (!Directory.Exists(effectiveWorkingDirectory))
                throw new DirectoryNotFoundException(
                    $"Pre-build working directory not found: '{command.WorkingDirectory}'.");

            try {
                var result = await ProcessRunner.RunShellCommand(
                    command.Command,
                    effectiveWorkingDirectory,
                    command.TimeoutSeconds,
                    throwOnFailure: false);

                if (result.ExitCode == 0)
                    continue;

                var message = BuildPreBuildError(command, displayName, workingDirectory, result.ExitCode);
                if (command.ContinueOnError)
                    Console.Error.WriteLine(message);
                else
                    throw new InvalidOperationException(message);
            }
            catch (Exception ex) when (command.ContinueOnError) {
                Console.Error.WriteLine(BuildPreBuildError(command, displayName, workingDirectory, null));
                Console.Error.WriteLine(ex.Message);
            }
            catch (InvalidOperationException) {
                throw;
            }
            catch (Exception ex) {
                throw new InvalidOperationException(
                    BuildPreBuildError(command, displayName, workingDirectory, null)
                    + Environment.NewLine
                    + ex.Message,
                    ex);
            }
        }
    }

    private static string BuildPreBuildError(
        PreBuildCommandConfig command,
        string displayName,
        string workingDirectory,
        int? exitCode) {
        var message = $"Pre-build command failed: {displayName}{Environment.NewLine}"
                    + $"Command: {command.Command}{Environment.NewLine}"
                    + $"Working directory: {workingDirectory}";

        if (exitCode is not null)
            message += $"{Environment.NewLine}Exit code: {exitCode}";

        return message;
    }

    private static string Sanitize(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars   = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}
