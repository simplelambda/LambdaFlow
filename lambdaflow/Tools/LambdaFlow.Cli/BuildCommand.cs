namespace LambdaFlow.Cli;

internal static class BuildCommand
{
    internal static async Task<int> Run(string[] args) {
        var options       = new CommandOptions(args);
        var projectDir    = Path.GetFullPath(options.Positionals.Count > 0 ? options.Positionals[0] : Directory.GetCurrentDirectory());
        var frameworkRoot = ProjectPaths.ResolveFrameworkRoot(options, projectDir);
        var config        = Program.ReadConfig(projectDir);
        var windowsX64    = config.GetWindowsX64();

        var backendSourceDir = Path.Combine(projectDir, config.DevelopmentBackendFolder);
        var frontendDir      = Path.Combine(projectDir, config.DevelopmentFrontendFolder);
        var resultRoot       = Path.Combine(projectDir, config.ResultFolder);
        var appDir           = Path.Combine(resultRoot, $"{Sanitize(config.AppName)}-{Sanitize(config.AppVersion)}", "windows-x64");

        if (Directory.Exists(appDir))
            Directory.Delete(appDir, recursive: true);

        Directory.CreateDirectory(appDir);

        await ProcessRunner.RunShellCommand(windowsX64.CompileCommand, backendSourceDir);

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

        File.Copy(Path.Combine(projectDir, "config.json"), Path.Combine(appDir, "config.json"), overwrite: true);
        FileSystemTools.CreatePak(frontendDir, Path.Combine(appDir, "frontend.pak"));
        IntegrityManifestWriter.Write(appDir);

        Console.WriteLine($"LambdaFlow app built at: {appDir}");
        return 0;
    }

    private static string Sanitize(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars   = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}
