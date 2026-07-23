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
        var target        = BuildTarget.Resolve(options.Get("--target"));
        var config        = Program.ReadConfig(projectDir);
        var archConfig    = config.GetArch(target);

        var backendSourceDir = Path.GetFullPath(Path.Combine(projectDir, config.DevelopmentBackendFolder));
        var frontendDir      = Path.GetFullPath(Path.Combine(projectDir, config.DevelopmentFrontendFolder));
        var resultRoot       = Path.GetFullPath(Path.Combine(projectDir, config.ResultFolder));
        var appDir           = Path.Combine(
            resultRoot,
            $"{Sanitize(config.AppName)}-{Sanitize(config.AppVersion)}",
            target.OutputFolder);

        EnsureDirectoryWithinProject(projectDir, backendSourceDir, nameof(config.DevelopmentBackendFolder));
        EnsureDirectoryWithinProject(projectDir, frontendDir, nameof(config.DevelopmentFrontendFolder));
        EnsureDirectoryWithinProject(projectDir, resultRoot, nameof(config.ResultFolder));

        await RunPreBuildCommands(config, projectDir);

        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);
        Directory.CreateDirectory(appDir);

        if (!string.IsNullOrWhiteSpace(archConfig.CompileCommand))
            await ProcessRunner.RunShellCommand(archConfig.CompileCommand, backendSourceDir);
        else
            Console.WriteLine("No backend compile command configured; copying backend source folder.");

        var backendOutputDir = Path.GetFullPath(Path.Combine(backendSourceDir, archConfig.CompileDirectory));
        EnsureDirectoryWithinProject(projectDir, backendOutputDir, "compileDirectory");
        FileSystemTools.CopyDirectory(backendOutputDir, Path.Combine(appDir, "backend"));

        await ProcessRunner.RunDotnet(
            "publish",
            ProjectPaths.HostProject(frameworkRoot, target),
            "-c",
            "Release",
            "-r",
            target.RuntimeIdentifier,
            "--self-contained",
            "true",
            "-o",
            appDir);

        var defaultExecutable = Path.Combine(appDir, target.HostBinaryName);
        var targetExecutable  = Path.Combine(appDir, Sanitize(config.AppName) + target.ExecutableExtension);
        if (!File.Exists(defaultExecutable))
            throw new FileNotFoundException(
                $"Published host executable not found at '{defaultExecutable}'.");
        if (!string.Equals(defaultExecutable, targetExecutable, StringComparison.OrdinalIgnoreCase))
            File.Move(defaultExecutable, targetExecutable, overwrite: true);

        WriteAppConfig(projectDir, appDir, forceDebug);
        CopyAppIcon(projectDir, appDir, config.AppIcon);
        FileSystemTools.CreatePak(frontendDir, Path.Combine(appDir, "frontend.pak"));
        IntegrityManifestWriter.Write(appDir);

        Console.WriteLine($"LambdaFlow app built for {target.OutputFolder}: {appDir}");
        return 0;
    }

    private static void EnsureDirectoryWithinProject(string projectDir, string candidate, string settingName) {
        var root = EnsureTrailingSeparator(Path.GetFullPath(projectDir));
        var path = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!path.StartsWith(root, comparison) && !string.Equals(path, root.TrimEnd(Path.DirectorySeparatorChar), comparison))
            throw new InvalidOperationException(
                $"{settingName} must resolve inside the project directory. Resolved path: '{path}'.");
    }

    private static string EnsureTrailingSeparator(string path) {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
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

        File.WriteAllText(
            targetPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static void CopyAppIcon(string projectDir, string appDir, string appIcon) {
        if (string.IsNullOrWhiteSpace(appIcon))
            return;

        var source = Path.GetFullPath(Path.Combine(projectDir, appIcon));
        EnsureDirectoryWithinProject(projectDir, source, "appIcon");
        if (!File.Exists(source))
            return;

        var target = Path.GetFullPath(Path.Combine(appDir, appIcon));
        var appRoot = EnsureTrailingSeparator(Path.GetFullPath(appDir));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(appRoot, comparison))
            throw new InvalidOperationException("appIcon must resolve inside the application output directory.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static async Task RunPreBuildCommands(LambdaFlowConfig config, string projectDir) {
        foreach (var command in config.Build.PreBuild.Where(c => c.Enabled)) {
            var displayName = string.IsNullOrWhiteSpace(command.Name) ? command.Command : command.Name;
            if (string.IsNullOrWhiteSpace(command.Command)) {
                var emptyMessage = $"Pre-build command failed: {displayName}{Environment.NewLine}Command is empty.";
                if (command.ContinueOnError) {
                    Console.Error.WriteLine(emptyMessage);
                    continue;
                }
                throw new InvalidOperationException(emptyMessage);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? projectDir
                : command.WorkingDirectory;
            var effectiveWorkingDirectory = Path.IsPathRooted(workingDirectory)
                ? workingDirectory
                : Path.GetFullPath(Path.Combine(projectDir, workingDirectory));

            EnsureDirectoryWithinProject(projectDir, effectiveWorkingDirectory, "preBuild.workingDirectory");
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
                    + Environment.NewLine + ex.Message,
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
        return exitCode is null
            ? message
            : message + $"{Environment.NewLine}Exit code: {exitCode}";
    }

    private static string Sanitize(string value) {
        const string portableInvalid = "<>:\"/\\|?*";
        var sanitized = new string(value
            .Select(ch => ch < 32 || portableInvalid.Contains(ch) ? '-' : ch)
            .ToArray())
            .TrimEnd(' ', '.');

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "LambdaFlowApp";

        var stem = sanitized.Split('.', 2)[0];
        string[] windowsReservedNames = {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (windowsReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            sanitized = "_" + sanitized;

        return sanitized;
    }
}
