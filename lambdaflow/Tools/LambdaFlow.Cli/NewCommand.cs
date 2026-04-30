using System.Text.Json;

namespace LambdaFlow.Cli;

internal static class NewCommand
{
    private enum ProjectLanguage
    {
        CSharp,
        Java,
        Python,
        Other
    }

    private enum FrontendTemplate
    {
        Basic,
        React
    }

    private sealed record LanguageDefaults(
        string   ExampleFolderName,
        string   CompileCommand,
        string   CompileDirectory,
        string   RunCommand,
        string[] RunArgs);

    internal static Task<int> Run(string[] args) {
        var options = new CommandOptions(args);
        if (options.Positionals.Count == 0)
            throw new ArgumentException(
                "Usage: lambdaflow new <AppName> [directory] [--framework <path>] [--language <csharp|java|python|other>] [--frontend <basic|react>] [--backend-compile-command <command>] [--backend-compile-directory <dir>] [--debug] [--self-contained]");

        var appName       = options.Positionals[0];
        var projectDir    = Path.GetFullPath(options.Positionals.Count > 1 ? options.Positionals[1] : appName);
        var frameworkRoot = ProjectPaths.ResolveFrameworkRoot(options, Directory.GetCurrentDirectory());
        var selfContained = options.HasFlag("--self-contained");
        var debugEnabled  = options.HasFlag("--debug");
        var language      = ParseLanguage(options.Get("--language"));
        var frontend      = ParseFrontend(options.Get("--frontend"));
        var defaults      = GetLanguageDefaults(language);

        var compileCommand = options.Get("--backend-compile-command");
        if (string.IsNullOrWhiteSpace(compileCommand))
            compileCommand = defaults.CompileCommand;

        var compileDirectory = options.Get("--backend-compile-directory");
        if (string.IsNullOrWhiteSpace(compileDirectory))
            compileDirectory = defaults.CompileDirectory;

        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
            throw new InvalidOperationException($"Target directory is not empty: '{projectDir}'.");

        Directory.CreateDirectory(projectDir);
        CreateConfig(projectDir, appName, compileCommand!, compileDirectory!, defaults, language, frontend, debugEnabled);
        CreateBackend(projectDir, frameworkRoot, language);
        CreateFrontend(projectDir, frameworkRoot, language, frontend);

        if (language != ProjectLanguage.Other)
            ProvisionLanguageSdk(projectDir, frameworkRoot, language);

        AdjustBackendForLanguage(projectDir, language);

        if (selfContained)
            CopyFrameworkSource(frameworkRoot, projectDir);

        CreateVsCodeTasks(projectDir, frameworkRoot, selfContained);
        CreateVsCodeLaunch(projectDir, appName);
        CreateVsCodeSettings(projectDir);

        Console.WriteLine($"LambdaFlow project created at: {projectDir}");
        Console.WriteLine($"Template language: {LanguageDisplayName(language)}");
        Console.WriteLine($"Frontend template: {FrontendDisplayName(frontend)}");
        Console.WriteLine("Open that folder in VS Code and run task: LambdaFlow: build app");

        return Task.FromResult(0);
    }

    private static ProjectLanguage ParseLanguage(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) return ProjectLanguage.CSharp;

        return raw.Trim().ToLowerInvariant() switch {
            "csharp" or "c#" or "cs" => ProjectLanguage.CSharp,
            "java"                     => ProjectLanguage.Java,
            "python" or "py"          => ProjectLanguage.Python,
            "other" or "otros"        => ProjectLanguage.Other,
            _ => throw new ArgumentException("Unsupported language. Allowed values: C#, Java, Python, Other.")
        };
    }

    private static FrontendTemplate ParseFrontend(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) return FrontendTemplate.Basic;

        return raw.Trim().ToLowerInvariant() switch {
            "basic" or "html" or "other" or "otro" or "otros" => FrontendTemplate.Basic,
            "react" or "vite-react"                           => FrontendTemplate.React,
            _ => throw new ArgumentException("Unsupported frontend. Allowed values: Basic, React.")
        };
    }

    private static LanguageDefaults GetLanguageDefaults(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => new LanguageDefaults(
                ExampleFolderName: "CSharp",
                CompileCommand: "dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin",
                CompileDirectory: "bin",
                RunCommand: "Backend.exe",
                RunArgs: Array.Empty<string>()),
            ProjectLanguage.Java => new LanguageDefaults(
                ExampleFolderName: "Java",
                CompileCommand: "mvn -q -DskipTests package",
                CompileDirectory: "target",
                RunCommand: "java",
                RunArgs: new[] { "-jar", "Backend.jar" }),
            ProjectLanguage.Python => new LanguageDefaults(
                ExampleFolderName: "Python",
                CompileCommand: "python build.py",
                CompileDirectory: "bin",
                RunCommand: "python",
                RunArgs: new[] { "backend.py" }),
            ProjectLanguage.Other => new LanguageDefaults(
                ExampleFolderName: "",
                CompileCommand: "",
                CompileDirectory: ".",
                RunCommand: "your-backend-command",
                RunArgs: Array.Empty<string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static string LanguageDisplayName(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => "C#",
            ProjectLanguage.Java   => "Java",
            ProjectLanguage.Python => "Python",
            ProjectLanguage.Other  => "Other",
            _ => language.ToString()
        };
    }

    private static string FrontendDisplayName(FrontendTemplate frontend) {
        return frontend switch {
            FrontendTemplate.Basic => "HTML basic",
            FrontendTemplate.React => "React",
            _ => frontend.ToString()
        };
    }

    private static string LanguageConfigValue(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => "csharp",
            ProjectLanguage.Java   => "java",
            ProjectLanguage.Python => "python",
            ProjectLanguage.Other  => "other",
            _ => language.ToString().ToLowerInvariant()
        };
    }

    private static void CopyFrameworkSource(string frameworkRoot, string projectDir) {
        CopySourceOnly(
            Path.Combine(frameworkRoot, "lambdaflow", "Core"),
            Path.Combine(projectDir,    "lambdaflow", "Core"));
        CopySourceOnly(
            Path.Combine(frameworkRoot, "lambdaflow", "Hosts", "Windows"),
            Path.Combine(projectDir,    "lambdaflow", "Hosts", "Windows"));
        CopySourceOnly(
            Path.Combine(frameworkRoot, "lambdaflow", "Tools", "LambdaFlow.Cli"),
            Path.Combine(projectDir,    "lambdaflow", "Tools", "LambdaFlow.Cli"));
    }

    private static void CopySourceOnly(string sourceDir, string targetDir) {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(targetDir);

        foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var rel   = Path.GetRelativePath(sourceDir, src);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Array.Exists(parts, p => p is "bin" or "obj")) continue;

            var dst = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }

    private static void CreateConfig(
        string           projectDir,
        string           appName,
        string           compileCommand,
        string           compileDirectory,
        LanguageDefaults defaults,
        ProjectLanguage  language,
        FrontendTemplate frontend,
        bool             debugEnabled) {
        var isReact = frontend == FrontendTemplate.React;
        var config = new {
            appName,
            appVersion                = "1.0.0",
            organizationName          = "LambdaFlow",
            appIcon                   = "app.ico",
            securityMode              = "Hardened",
            ipcTransport              = "NamedPipe",
            developmentBackendFolder  = "backend",
            developmentFrontendFolder = isReact ? "frontend/dist" : "frontend",
            resultFolder              = "Results",
            frontendInitialHTML       = "index.html",
            build = new {
                preBuild = isReact
                    ? new[] {
                        new {
                            name             = "Install React dependencies",
                            command          = "if not exist node_modules\\.bin\\vite.cmd npm install",
                            workingDirectory = "frontend",
                            enabled          = true,
                            continueOnError  = false,
                            timeoutSeconds   = (int?)null
                        },
                        new {
                            name             = "Build React frontend",
                            command          = "npm run build",
                            workingDirectory = "frontend",
                            enabled          = true,
                            continueOnError  = false,
                            timeoutSeconds   = (int?)null
                        }
                    }
                    : Array.Empty<object>()
            },
            debug = new {
                enabled                  = debugEnabled,
                frontendDevTools         = debugEnabled,
                openFrontendDevToolsOnStart = debugEnabled,
                captureFrontendConsole   = debugEnabled,
                showBackendConsole       = debugEnabled,
                backendLogLevel          = debugEnabled ? "debug" : "info"
            },
            backend = new {
                language         = LanguageConfigValue(language),
                command          = defaults.RunCommand,
                args             = defaults.RunArgs,
                workingDirectory = "backend"
            },
            platforms = new {
                windows = new {
                    archs = new {
                        x64 = new {
                            compileCommand,
                            compileDirectory,
                            runCommand = defaults.RunCommand,
                            runArgs    = defaults.RunArgs
                        }
                    }
                }
            },
            window = new {
                title     = appName,
                width     = 1000,
                height    = 700,
                minWidth  = 640,
                minHeight = 480,
                maxWidth  = 0,
                maxHeight = 0
            }
        };

        var text = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true })
                 + Environment.NewLine;
        FileSystemTools.WriteFile(Path.Combine(projectDir, "config.json"), text);
    }

    private static void CreateBackend(string projectDir, string frameworkRoot, ProjectLanguage language) {
        if (language == ProjectLanguage.Other) {
            CreateGenericBackend(projectDir);
            return;
        }

        var defaults         = GetLanguageDefaults(language);
        var sourceBackendDir = Path.Combine(frameworkRoot, "Examples", defaults.ExampleFolderName, "backend");
        var targetBackendDir = Path.Combine(projectDir, "backend");

        CopyTemplateDirectory(sourceBackendDir, targetBackendDir);
    }

    private static void CreateFrontend(
        string           projectDir,
        string           frameworkRoot,
        ProjectLanguage  language,
        FrontendTemplate frontend) {
        if (frontend == FrontendTemplate.React) {
            CreateReactFrontend(projectDir, frameworkRoot);
            return;
        }

        var defaults          = GetLanguageDefaults(language == ProjectLanguage.Other ? ProjectLanguage.CSharp : language);
        var sourceFrontendDir = Path.Combine(frameworkRoot, "Examples", defaults.ExampleFolderName, "frontend");
        var targetFrontendDir = Path.Combine(projectDir, "frontend");

        CopyTemplateDirectory(sourceFrontendDir, targetFrontendDir);
    }

    private static void CreateGenericBackend(string projectDir) {
        FileSystemTools.WriteFile(Path.Combine(projectDir, "backend", "README.md"), """
        # Backend

        Put your backend executable, script, or build output here.

        The generated config uses:

        - `compileCommand`: empty, so LambdaFlow copies this folder as-is.
        - `compileDirectory`: `.`
        - `runCommand`: `your-backend-command`

        Edit `config.json` before running the app:

        - `platforms.windows.archs.x64.compileCommand`
        - `platforms.windows.archs.x64.compileDirectory`
        - `platforms.windows.archs.x64.runCommand`
        - `platforms.windows.archs.x64.runArgs`

        Your backend must read and write one JSON envelope per line using the
        selected LambdaFlow transport. Use `stderr` for logs when StdIO is the
        protocol transport.
        """);
    }

    private static void CreateReactFrontend(string projectDir, string frameworkRoot) {
        var frontendDir = Path.Combine(projectDir, "frontend");
        var sdkSource   = Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "JavaScript", "lambdaflow.js");

        if (!File.Exists(sdkSource))
            throw new FileNotFoundException($"JavaScript SDK source file not found at '{sdkSource}'.");

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "package.json"), """
        {
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "preview": "vite preview"
          },
          "dependencies": {
            "react": "latest",
            "react-dom": "latest"
          },
          "devDependencies": {
            "@vitejs/plugin-react": "latest",
            "vite": "latest"
          }
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "vite.config.js"), """
        import { defineConfig } from 'vite';
        import react from '@vitejs/plugin-react';

        export default defineConfig({
          base: './',
          plugins: [react()]
        });
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "index.html"), """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>LambdaFlow React App</title>
            <script src="./lambdaflow.js"></script>
          </head>
          <body>
            <div id="root"></div>
            <script type="module" src="/src/main.jsx"></script>
          </body>
        </html>
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "main.jsx"), """
        import React from 'react';
        import { createRoot } from 'react-dom/client';
        import App from './App.jsx';
        import './styles/globals.css';

        createRoot(document.getElementById('root')).render(
          <React.StrictMode>
            <App />
          </React.StrictMode>
        );
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "App.jsx"), """
        import { useMemo, useState } from 'react';
        import { getHostStatus, pingBackend } from './services/lambdaflowApi.js';
        import HomePage from './pages/HomePage.jsx';

        export default function App() {
          const hostStatus = useMemo(() => getHostStatus(), []);
          const [pingResult, setPingResult] = useState('');
          const [isPinging, setIsPinging] = useState(false);

          async function handlePing() {
            setIsPinging(true);
            setPingResult('');
            try {
              const result = await pingBackend();
              setPingResult(typeof result === 'string' ? result : JSON.stringify(result));
            }
            catch (error) {
              setPingResult(error instanceof Error ? error.message : String(error));
            }
            finally {
              setIsPinging(false);
            }
          }

          return (
            <HomePage
              hostStatus={hostStatus}
              pingResult={pingResult}
              isPinging={isPinging}
              onPing={handlePing}
            />
          );
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "pages", "HomePage.jsx"), """
        export default function HomePage({ hostStatus, pingResult, isPinging, onPing }) {
          return (
            <main className="shell">
              <section className="panel">
                <p className="eyebrow">LambdaFlow</p>
                <h1>React frontend ready</h1>
                <p className="lead">
                  This Vite app is built before LambdaFlow packages the desktop app.
                </p>

                <div className={hostStatus.available ? 'status ok' : 'status warn'}>
                  {hostStatus.message}
                </div>

                <button type="button" onClick={onPing} disabled={!hostStatus.available || isPinging}>
                  {isPinging ? 'Pinging...' : 'Ping backend'}
                </button>

                {pingResult && <pre className="result">{pingResult}</pre>}
              </section>
            </main>
          );
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "services", "lambdaflowApi.js"), """
        export function getHostStatus() {
          if (!window.send) {
            return {
              available: false,
              message: 'window.send is not available. Run this app inside the LambdaFlow host.'
            };
          }

          if (!window.LambdaFlow) {
            return {
              available: false,
              message: 'LambdaFlow JavaScript SDK is not loaded.'
            };
          }

          return {
            available: true,
            message: 'LambdaFlow host detected.'
          };
        }

        export async function pingBackend() {
          if (!window.LambdaFlow)
            throw new Error('LambdaFlow JavaScript SDK is not loaded.');

          return window.LambdaFlow.request('backend.ping', null, 5000);
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "styles", "globals.css"), """
        :root {
          color: #17202a;
          background: #f5f7fb;
          font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        }

        * {
          box-sizing: border-box;
        }

        body {
          margin: 0;
        }

        button {
          border: 0;
          border-radius: 6px;
          background: #1264a3;
          color: white;
          cursor: pointer;
          font: inherit;
          padding: 10px 14px;
        }

        button:disabled {
          cursor: default;
          opacity: 0.55;
        }

        .shell {
          min-height: 100vh;
          display: grid;
          place-items: center;
          padding: 32px;
        }

        .panel {
          width: min(620px, 100%);
          border: 1px solid #d8dee9;
          border-radius: 8px;
          background: white;
          padding: 28px;
          box-shadow: 0 20px 50px rgba(15, 23, 42, 0.08);
        }

        .eyebrow {
          color: #1264a3;
          font-size: 12px;
          font-weight: 700;
          letter-spacing: 0.08em;
          margin: 0 0 8px;
          text-transform: uppercase;
        }

        h1 {
          font-size: 34px;
          line-height: 1.1;
          margin: 0;
        }

        .lead {
          color: #526070;
          line-height: 1.6;
          margin: 14px 0 22px;
        }

        .status {
          border-radius: 6px;
          margin-bottom: 16px;
          padding: 10px 12px;
        }

        .status.ok {
          background: #e8f5ee;
          color: #17633a;
        }

        .status.warn {
          background: #fff4df;
          color: #7a4d00;
        }

        .result {
          background: #101820;
          border-radius: 6px;
          color: #f8fafc;
          margin: 16px 0 0;
          overflow: auto;
          padding: 12px;
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "components", "common", ".gitkeep"), "");
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "public", "lambdaflow.js"), File.ReadAllText(sdkSource));
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "README.md"), """
        # React frontend

        Install dependencies once if you want to run Vite directly:

        ```bash
        npm install
        ```

        LambdaFlow installs dependencies when `node_modules` is missing and runs
        `npm run build` automatically before packaging because `config.json`
        contains `build.preBuild` commands. The packaged app uses `frontend/dist`
        as its frontend folder.
        """);
    }

    private static void CopyTemplateDirectory(string sourceDir, string targetDir) {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Template directory not found: '{sourceDir}'.");

        Directory.CreateDirectory(targetDir);

        foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var rel   = Path.GetRelativePath(sourceDir, src);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Array.Exists(parts, IsBuildArtifactDirectory))
                continue;

            var dst = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }

    private static bool IsBuildArtifactDirectory(string part) {
        return part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("target", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Results", StringComparison.OrdinalIgnoreCase)
            || part.Equals("__pycache__", StringComparison.OrdinalIgnoreCase)
            || part.Equals(".pytest_cache", StringComparison.OrdinalIgnoreCase);
    }

    private static void ProvisionLanguageSdk(string projectDir, string frameworkRoot, ProjectLanguage language) {
        var sdkSourcePath = SdkSourcePath(frameworkRoot, language);
        if (!File.Exists(sdkSourcePath))
            throw new FileNotFoundException($"SDK source file not found at '{sdkSourcePath}'.");

        var sdkTargetPath = SdkTargetPath(projectDir, language);
        FileSystemTools.WriteFile(sdkTargetPath, File.ReadAllText(sdkSourcePath));
    }

    private static string SdkSourcePath(string frameworkRoot, ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "CSharp", "LambdaFlow.cs"),
            ProjectLanguage.Java   => Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "Java", "LambdaFlow.java"),
            ProjectLanguage.Python => Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "Python", "lambdaflow.py"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static string SdkTargetPath(string projectDir, ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => Path.Combine(projectDir, "lambdaflow", "Sdk", "CSharp", "LambdaFlow.cs"),
            ProjectLanguage.Java   => Path.Combine(projectDir, "lambdaflow", "Sdk", "Java", "LambdaFlow.java"),
            ProjectLanguage.Python => Path.Combine(projectDir, "lambdaflow", "Sdk", "Python", "lambdaflow.py"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static void AdjustBackendForLanguage(string projectDir, ProjectLanguage language) {
        switch (language) {
            case ProjectLanguage.CSharp:
                AdjustCSharpBackendSdkReference(projectDir);
                break;
            case ProjectLanguage.Java:
                AdjustJavaBackendSdkReference(projectDir);
                break;
            case ProjectLanguage.Python:
                AdjustPythonBackendSdkReference(projectDir);
                break;
            case ProjectLanguage.Other:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.");
        }
    }

    private static void AdjustCSharpBackendSdkReference(string projectDir) {
        var backendDir = Path.Combine(projectDir, "backend");
        var csprojPath = Directory.EnumerateFiles(backendDir, "*.csproj", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (csprojPath is null) return;

        var includePath = "../lambdaflow/Sdk/CSharp/LambdaFlow.cs";
        var csprojText  = File.ReadAllText(csprojPath)
            .Replace("..\\..\\..\\lambdaflow\\Sdk\\CSharp\\LambdaFlow.cs", includePath, StringComparison.Ordinal)
            .Replace("../../../lambdaflow/Sdk/CSharp/LambdaFlow.cs", includePath, StringComparison.Ordinal)
            .Replace("Sdk/LambdaFlow.cs", includePath, StringComparison.Ordinal)
            .Replace("Sdk\\LambdaFlow.cs", includePath, StringComparison.Ordinal);

        if (!csprojText.Contains(includePath, StringComparison.OrdinalIgnoreCase)
            && !csprojText.Contains("..\\lambdaflow\\Sdk\\CSharp\\LambdaFlow.cs", StringComparison.OrdinalIgnoreCase)) {
            csprojText = csprojText.Replace(
                "</Project>",
                "  <ItemGroup>\n    <Compile Include=\"../lambdaflow/Sdk/CSharp/LambdaFlow.cs\" Link=\"Sdk/LambdaFlow.cs\" />\n  </ItemGroup>\n\n</Project>",
                StringComparison.Ordinal);
        }

        File.WriteAllText(csprojPath, csprojText);
    }

    private static void AdjustJavaBackendSdkReference(string projectDir) {
        var backendDir  = Path.Combine(projectDir, "backend");
        var localSdkDir = Path.Combine(backendDir, "src", "main", "java", "lambdaflow");
        if (Directory.Exists(localSdkDir))
            Directory.Delete(localSdkDir, recursive: true);

        var pomPath = Path.Combine(backendDir, "pom.xml");
        if (!File.Exists(pomPath)) return;

        var pomText = File.ReadAllText(pomPath);
        if (pomText.Contains("build-helper-maven-plugin", StringComparison.Ordinal)) return;

        var plugin = """
            <plugin>
                <groupId>org.codehaus.mojo</groupId>
                <artifactId>build-helper-maven-plugin</artifactId>
                <version>3.6.0</version>
                <executions>
                    <execution>
                        <id>add-lambdaflow-sdk-source</id>
                        <phase>generate-sources</phase>
                        <goals>
                            <goal>add-source</goal>
                        </goals>
                        <configuration>
                            <sources>
                                <source>${project.basedir}/../lambdaflow/Sdk/Java</source>
                            </sources>
                        </configuration>
                    </execution>
                </executions>
            </plugin>

        """;

        pomText = pomText.Replace("<plugins>", "<plugins>\n" + plugin, StringComparison.Ordinal);
        File.WriteAllText(pomPath, pomText);
    }

    private static void AdjustPythonBackendSdkReference(string projectDir) {
        var backendDir = Path.Combine(projectDir, "backend");

        var localSdkPath = Path.Combine(backendDir, "lambdaflow.py");
        if (File.Exists(localSdkPath))
            File.Delete(localSdkPath);

        var backendPyPath = Path.Combine(backendDir, "backend.py");
        if (File.Exists(backendPyPath)) {
            var backendText = File.ReadAllText(backendPyPath);
            if (backendText.Contains("import lambdaflow as lf", StringComparison.Ordinal)
                && !backendText.Contains("_SDK_DIR", StringComparison.Ordinal)) {
                backendText = backendText.Replace(
                    "import lambdaflow as lf",
                    "import pathlib\nimport sys\n\n_PROJECT_ROOT = pathlib.Path(__file__).resolve().parents[1]\n_SDK_DIR      = _PROJECT_ROOT / \"lambdaflow\" / \"Sdk\" / \"Python\"\nif str(_SDK_DIR) not in sys.path:\n    sys.path.insert(0, str(_SDK_DIR))\n\nimport lambdaflow as lf",
                    StringComparison.Ordinal);

                File.WriteAllText(backendPyPath, backendText);
            }
        }

        var buildPyPath = Path.Combine(backendDir, "build.py");
        if (File.Exists(buildPyPath)) {
            var buildText = File.ReadAllText(buildPyPath);
            if (!buildText.Contains("sdk_source", StringComparison.Ordinal)) {
                buildText = buildText.Replace(
                    "print(f\"Copied {len(glob.glob('bin/*.py'))} python files into bin/\")",
                    "sdk_source = os.path.normpath(os.path.join(\"..\", \"lambdaflow\", \"Sdk\", \"Python\", \"lambdaflow.py\"))\nif not os.path.isfile(sdk_source):\n    raise FileNotFoundError(f\"LambdaFlow Python SDK not found at {sdk_source}\")\nshutil.copy(sdk_source, os.path.join(\"bin\", \"lambdaflow.py\"))\n\nprint(f\"Copied {len(glob.glob('bin/*.py'))} python files into bin/\")",
                    StringComparison.Ordinal);

                File.WriteAllText(buildPyPath, buildText);
            }
        }
    }

    private static void CreateVsCodeTasks(string projectDir, string frameworkRoot, bool selfContained) {
        var cliProject = selfContained
            ? "${workspaceFolder}/lambdaflow/Tools/LambdaFlow.Cli/LambdaFlow.Cli.csproj"
            : ProjectPaths.CliProject(frameworkRoot).Replace('\\', '/');
        var framework  = selfContained
            ? "${workspaceFolder}"
            : frameworkRoot.Replace('\\', '/');

        FileSystemTools.WriteFile(Path.Combine(projectDir, ".vscode", "tasks.json"), $$"""
        {
          "version": "2.0.0",
          "tasks": [
            {
              "label": "LambdaFlow: build app",
              "type": "process",
              "command": "dotnet",
              "args": [
                "run",
                "--project",
                "{{cliProject}}",
                "--",
                "build",
                "${workspaceFolder}",
                "--framework",
                "{{framework}}"
              ],
              "group": "build",
              "problemMatcher": []
            }
          ]
        }
        """);
    }

    private static void CreateVsCodeLaunch(string projectDir, string appName) {
        var sanitized = SanitizeFileName(appName);
        FileSystemTools.WriteFile(Path.Combine(projectDir, ".vscode", "launch.json"), $$"""
        {
          "version": "0.2.0",
          "configurations": [
            {
              "name": "LambdaFlow: run app",
              "type": "coreclr",
              "request": "launch",
              "preLaunchTask": "LambdaFlow: build app",
              "program": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/windows-x64/{{sanitized}}.exe",
              "args": [],
              "cwd": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/windows-x64",
              "console": "internalConsole",
              "stopAtEntry": false
            }
          ]
        }
        """);
    }

    private static void CreateVsCodeSettings(string projectDir) {
        FileSystemTools.WriteFile(Path.Combine(projectDir, ".vscode", "settings.json"), """
        {
          "files.exclude": {
            "**/bin":         true,
            "**/obj":         true,
            "**/target":      true,
            "**/node_modules": true,
            "**/__pycache__": true,
            "**/Results":     true
          }
        }
        """);
    }

    private static string SanitizeFileName(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars   = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}
