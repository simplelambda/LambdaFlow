using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LambdaFlow.Cli;

internal sealed class LambdaFlowConfig
{
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "LambdaFlowApp";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = "LambdaFlow";

    [JsonPropertyName("developmentBackendFolder")]
    public string DevelopmentBackendFolder { get; set; } = "backend";

    [JsonPropertyName("developmentFrontendFolder")]
    public string DevelopmentFrontendFolder { get; set; } = "frontend";

    [JsonPropertyName("resultFolder")]
    public string ResultFolder { get; set; } = "Results";

    [JsonPropertyName("frontendInitialHTML")]
    public string FrontendInitialHTML { get; set; } = "index.html";

    [JsonPropertyName("build")]
    public BuildConfig Build { get; set; } = new BuildConfig();

    [JsonPropertyName("debug")]
    public DebugConfig Debug { get; set; } = new DebugConfig();

    [JsonPropertyName("platforms")]
    public Dictionary<string, PlatformConfig> Platforms { get; set; } = new Dictionary<string, PlatformConfig>();

    internal ArchConfig GetWindowsX64() {
        if (!Platforms.TryGetValue("windows", out var windows))
            throw new InvalidOperationException("config.json must define platforms.windows.");

        if (!windows.Archs.TryGetValue("x64", out var x64))
            throw new InvalidOperationException("config.json must define platforms.windows.archs.x64.");

        return x64;
    }
}

internal sealed class BuildConfig
{
    [JsonPropertyName("preBuild")]
    public List<PreBuildCommandConfig> PreBuild { get; set; } = new List<PreBuildCommandConfig>();
}

internal sealed class PreBuildCommandConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; set; } = false;

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }
}

internal sealed class DebugConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("frontendDevTools")]
    public bool FrontendDevTools { get; set; } = false;

    [JsonPropertyName("openFrontendDevToolsOnStart")]
    public bool OpenFrontendDevToolsOnStart { get; set; } = false;

    [JsonPropertyName("captureFrontendConsole")]
    public bool CaptureFrontendConsole { get; set; } = false;

    [JsonPropertyName("showBackendConsole")]
    public bool ShowBackendConsole { get; set; } = false;

    [JsonPropertyName("backendLogLevel")]
    public string BackendLogLevel { get; set; } = "info";
}

internal sealed class PlatformConfig
{
    [JsonPropertyName("archs")]
    public Dictionary<string, ArchConfig> Archs { get; set; } = new Dictionary<string, ArchConfig>();
}

internal sealed class ArchConfig
{
    [JsonPropertyName("compileCommand")]
    public string CompileCommand { get; set; } = "dotnet publish -c Release -r win-x64 --self-contained false -o bin";

    [JsonPropertyName("compileDirectory")]
    public string CompileDirectory { get; set; } = "bin";

    [JsonPropertyName("runCommand")]
    public string RunCommand { get; set; } = "Backend.exe";

    [JsonPropertyName("runArgs")]
    public List<string> RunArgs { get; set; } = new List<string>();
}
