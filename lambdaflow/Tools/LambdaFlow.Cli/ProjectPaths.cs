namespace LambdaFlow.Cli;

internal static class ProjectPaths
{
    internal static string ResolveFrameworkRoot(CommandOptions options, string startDirectory) {
        var explicitRoot = options.Get("--framework");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            return ValidateFrameworkRoot(Path.GetFullPath(explicitRoot));

        var envRoot = Environment.GetEnvironmentVariable("LAMBDAFLOW_HOME");
        if (!string.IsNullOrWhiteSpace(envRoot))
            return ValidateFrameworkRoot(Path.GetFullPath(envRoot));

        var current = new DirectoryInfo(startDirectory);
        while (current is not null) {
            var candidate = Path.Combine(current.FullName, "lambdaflow", "Hosts", "Windows", "lambdaflow.windows.csproj");
            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("LambdaFlow framework root could not be found. Pass --framework or set LAMBDAFLOW_HOME.");
    }

    internal static string HostProject(string frameworkRoot) {
        return Path.Combine(frameworkRoot, "lambdaflow", "Hosts", "Windows", "lambdaflow.windows.csproj");
    }

    internal static string CliProject(string frameworkRoot) {
        return Path.Combine(frameworkRoot, "lambdaflow", "Tools", "LambdaFlow.Cli", "LambdaFlow.Cli.csproj");
    }

    private static string ValidateFrameworkRoot(string root) {
        var hostProject = HostProject(root);
        if (!File.Exists(hostProject))
            throw new FileNotFoundException($"LambdaFlow Windows host project not found at '{hostProject}'.");

        return root;
    }
}
