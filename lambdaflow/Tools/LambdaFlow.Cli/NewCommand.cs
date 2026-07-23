using System.Text.Json;

namespace LambdaFlow.Cli;

internal static class NewCommand
{
    private enum ProjectLanguage
    {
        CSharp,
        Java,
        Python,
        Node,
        Go,
        Other
    }

    private enum FrontendTemplate
    {
        Basic,
        React,
        Vue,
        Svelte
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
                "Usage: lambdaflow new <AppName> [directory] [--framework <path>] [--language <csharp|java|python|node|go|other>] [--frontend <basic|react|vue|svelte>] [--backend-compile-command <command>] [--backend-compile-directory <dir>] [--debug] [--self-contained]");

        var appName       = options.Positionals[0];
        var projectDir    = Path.GetFullPath(options.Positionals.Count > 1 ? options.Positionals[1] : appName);
        var frameworkRoot = ProjectPaths.ResolveFrameworkRoot(options, Directory.GetCurrentDirectory());
        var selfContained = options.HasFlag("--self-contained");
        var debugEnabled  = options.HasFlag("--debug");
        var language      = ParseLanguage(options.Get("--language"));
        var frontend      = ParseFrontend(options.Get("--frontend"));
        var defaults      = GetLanguageDefaults(language);

        var compileCommandOverride = options.Get("--backend-compile-command");
        var windowsCompileCommand = string.IsNullOrWhiteSpace(compileCommandOverride)
            ? CompileCommandFor(language, "win-x64")
            : compileCommandOverride;
        var linuxCompileCommand = string.IsNullOrWhiteSpace(compileCommandOverride)
            ? CompileCommandFor(language, "linux-x64")
            : compileCommandOverride;

        var compileDirectoryOverride = options.Get("--backend-compile-directory");
        var hasCompileDirectoryOverride = !string.IsNullOrWhiteSpace(compileDirectoryOverride);
        var useTargetSpecificCompileDirectory =
            (language is ProjectLanguage.CSharp or ProjectLanguage.Node or ProjectLanguage.Go)
            && string.IsNullOrWhiteSpace(compileCommandOverride)
            && !hasCompileDirectoryOverride;
        var compileDirectory = compileDirectoryOverride;
        if (!hasCompileDirectoryOverride)
            compileDirectory = defaults.CompileDirectory;

        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
            throw new InvalidOperationException($"Target directory is not empty: '{projectDir}'.");

        Directory.CreateDirectory(projectDir);
        CreateConfig(
            projectDir,
            appName,
            windowsCompileCommand!,
            linuxCompileCommand!,
            compileDirectory!,
            useTargetSpecificCompileDirectory,
            defaults,
            language,
            frontend,
            debugEnabled);
        CreateBackend(projectDir, frameworkRoot, language);
        CreateFrontend(projectDir, frameworkRoot, frontend);

        if (HasCanonicalBackendSdk(language))
            ProvisionLanguageSdk(projectDir, frameworkRoot, language);

        AdjustBackendForLanguage(projectDir, language);

        if (selfContained)
            CopyFrameworkSource(frameworkRoot, projectDir);

        CreateVsCodeTasks(projectDir, frameworkRoot, selfContained);
        CreateVsCodeLaunch(projectDir, appName);
        CreateVsCodeSettings(projectDir);
        CreateProjectGitIgnore(projectDir);

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
            "node" or "nodejs" or "javascript" or "js" => ProjectLanguage.Node,
            "go" or "golang"           => ProjectLanguage.Go,
            "other" or "otros"        => ProjectLanguage.Other,
            _ => throw new ArgumentException("Unsupported language. Allowed values: C#, Java, Python, Node.js, Go, Other.")
        };
    }

    private static FrontendTemplate ParseFrontend(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) return FrontendTemplate.Basic;

        return raw.Trim().ToLowerInvariant() switch {
            "basic" or "html" or "other" or "otro" or "otros" => FrontendTemplate.Basic,
            "react" or "vite-react"                           => FrontendTemplate.React,
            "vue" or "vite-vue"                               => FrontendTemplate.Vue,
            "svelte" or "vite-svelte"                         => FrontendTemplate.Svelte,
            _ => throw new ArgumentException("Unsupported frontend. Allowed values: Basic, React, Vue, Svelte.")
        };
    }

    private static LanguageDefaults GetLanguageDefaults(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => new LanguageDefaults(
                ExampleFolderName: "CSharp",
                CompileCommand: "dotnet publish Backend.csproj -c Release -r win-x64 --self-contained true -o bin/win-x64",
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
            ProjectLanguage.Node => new LanguageDefaults(
                ExampleFolderName: "Node",
                CompileCommand: "node build.mjs win-x64",
                CompileDirectory: "bin",
                RunCommand: "node",
                RunArgs: new[] { "backend.mjs" }),
            ProjectLanguage.Go => new LanguageDefaults(
                ExampleFolderName: "Go",
                CompileCommand: "go run tools/build.go --target win-x64",
                CompileDirectory: "bin",
                RunCommand: "Backend.exe",
                RunArgs: Array.Empty<string>()),
            ProjectLanguage.Other => new LanguageDefaults(
                ExampleFolderName: "",
                CompileCommand: "",
                CompileDirectory: ".",
                RunCommand: "your-backend-command",
                RunArgs: Array.Empty<string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static string CompileCommandFor(ProjectLanguage language, string runtimeIdentifier) {
        return language switch {
            ProjectLanguage.CSharp =>
                $"dotnet publish Backend.csproj -c Release -r {runtimeIdentifier} --self-contained true -o bin/{runtimeIdentifier}",
            ProjectLanguage.Java => "mvn -q -DskipTests package",
            ProjectLanguage.Python => runtimeIdentifier.StartsWith("linux-", StringComparison.Ordinal)
                ? "python3 build.py"
                : "python build.py",
            ProjectLanguage.Node => $"node build.mjs {runtimeIdentifier}",
            ProjectLanguage.Go => $"go run tools/build.go --target {runtimeIdentifier}",
            ProjectLanguage.Other => "",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static string RunCommandFor(ProjectLanguage language, string platform) {
        return language switch {
            ProjectLanguage.CSharp => platform == "windows" ? "Backend.exe" : "Backend",
            ProjectLanguage.Java => "java",
            ProjectLanguage.Python => platform == "windows" ? "python" : "python3",
            ProjectLanguage.Node => "node",
            ProjectLanguage.Go => platform == "windows" ? "Backend.exe" : "Backend",
            ProjectLanguage.Other => "your-backend-command",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported language template.")
        };
    }

    private static string LanguageDisplayName(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => "C#",
            ProjectLanguage.Java   => "Java",
            ProjectLanguage.Python => "Python",
            ProjectLanguage.Node   => "Node.js",
            ProjectLanguage.Go     => "Go",
            ProjectLanguage.Other  => "Other",
            _ => language.ToString()
        };
    }

    private static string FrontendDisplayName(FrontendTemplate frontend) {
        return frontend switch {
            FrontendTemplate.Basic => "HTML basic",
            FrontendTemplate.React => "React",
            FrontendTemplate.Vue => "Vue",
            FrontendTemplate.Svelte => "Svelte",
            _ => frontend.ToString()
        };
    }

    private static string LanguageConfigValue(ProjectLanguage language) {
        return language switch {
            ProjectLanguage.CSharp => "csharp",
            ProjectLanguage.Java   => "java",
            ProjectLanguage.Python => "python",
            ProjectLanguage.Node   => "node",
            ProjectLanguage.Go     => "go",
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
            Path.Combine(frameworkRoot, "lambdaflow", "Hosts", "Linux"),
            Path.Combine(projectDir,    "lambdaflow", "Hosts", "Linux"));
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
        string           windowsCompileCommand,
        string           linuxCompileCommand,
        string           compileDirectory,
        bool             useTargetSpecificCompileDirectory,
        LanguageDefaults defaults,
        ProjectLanguage  language,
        FrontendTemplate frontend,
        bool             debugEnabled) {
        var usesFrontendBuild = frontend != FrontendTemplate.Basic;
        var frontendName = FrontendDisplayName(frontend);
        var config = new {
            appName,
            appVersion                = "1.0.0",
            organizationName          = "LambdaFlow",
            appIcon                   = "app.ico",
            securityMode              = "Hardened",
            ipcTransport              = "Auto",
            developmentBackendFolder  = "backend",
            developmentFrontendFolder = usesFrontendBuild ? "frontend/dist" : "frontend",
            resultFolder              = "Results",
            frontendInitialHTML       = "index.html",
            build = new {
                preBuild = usesFrontendBuild
                    ? new[] {
                        new {
                            name             = $"Install {frontendName} dependencies",
                            command          = "npm install --no-audit --no-fund",
                            workingDirectory = "frontend",
                            enabled          = true,
                            continueOnError  = false,
                            timeoutSeconds   = (int?)null
                        },
                        new {
                            name             = $"Build {frontendName} frontend",
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
                            compileCommand = windowsCompileCommand,
                            compileDirectory = CompileDirectoryFor("win-x64", compileDirectory, useTargetSpecificCompileDirectory),
                            runCommand = RunCommandFor(language, "windows"),
                            runArgs    = defaults.RunArgs
                        },
                        arm64 = new {
                            compileCommand = CompileCommandFor(language, "win-arm64"),
                            compileDirectory = CompileDirectoryFor("win-arm64", compileDirectory, useTargetSpecificCompileDirectory),
                            runCommand = RunCommandFor(language, "windows"),
                            runArgs    = defaults.RunArgs
                        }
                    }
                },
                linux = new {
                    archs = new {
                        x64 = new {
                            compileCommand = linuxCompileCommand,
                            compileDirectory = CompileDirectoryFor("linux-x64", compileDirectory, useTargetSpecificCompileDirectory),
                            runCommand = RunCommandFor(language, "linux"),
                            runArgs    = defaults.RunArgs
                        },
                        arm64 = new {
                            compileCommand = CompileCommandFor(language, "linux-arm64"),
                            compileDirectory = CompileDirectoryFor("linux-arm64", compileDirectory, useTargetSpecificCompileDirectory),
                            runCommand = RunCommandFor(language, "linux"),
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

    private static string CompileDirectoryFor(
        string runtimeIdentifier,
        string configuredDirectory,
        bool useTargetSpecificDirectory) {
        return useTargetSpecificDirectory
            ? $"bin/{runtimeIdentifier}"
            : configuredDirectory;
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
        FrontendTemplate frontend) {
        switch (frontend) {
            case FrontendTemplate.React:
                CreateReactFrontend(projectDir, frameworkRoot);
                return;
            case FrontendTemplate.Vue:
                CreateVueFrontend(projectDir, frameworkRoot);
                return;
            case FrontendTemplate.Svelte:
                CreateSvelteFrontend(projectDir, frameworkRoot);
                return;
        }

        var sourceFrontendDir = Path.Combine(frameworkRoot, "Examples", "CSharp", "frontend");
        var targetFrontendDir = Path.Combine(projectDir, "frontend");

        CopyTemplateDirectory(sourceFrontendDir, targetFrontendDir);
        CopyJavaScriptSdk(frameworkRoot, Path.Combine(targetFrontendDir, "lambdaflow.js"));
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
        - `platforms.linux.archs.x64.compileCommand`
        - `platforms.linux.archs.x64.compileDirectory`
        - `platforms.linux.archs.x64.runCommand`
        - `platforms.linux.archs.x64.runArgs`

        Your backend must read and write one JSON envelope per line using the
        selected LambdaFlow transport. Use `stderr` for logs when StdIO is the
        protocol transport.
        """);
    }

    private static void CreateReactFrontend(string projectDir, string frameworkRoot) {
        var frontendDir = Path.Combine(projectDir, "frontend");

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "package.json"), """
        {
          "type": "module",
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "preview": "vite preview"
          },
          "dependencies": {
            "react": "19.2.8",
            "react-dom": "19.2.8"
          },
          "devDependencies": {
            "@vitejs/plugin-react": "6.0.4",
            "vite": "8.1.5"
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
            <script src="/lambdaflow.js"></script>
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
        import { useEffect, useMemo, useState } from 'react';
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

          useEffect(() => {
            if (hostStatus.available) void handlePing();
          }, [hostStatus.available]);

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
        function sdk() {
          if (!window.LambdaFlow) {
            throw new Error('LambdaFlow JavaScript SDK is not loaded. Load lambdaflow.js before the React entrypoint.');
          }

          if (typeof window.send !== 'function') {
            throw new Error('window.send is not available. Run this app inside the LambdaFlow host.');
          }

          return window.LambdaFlow;
        }

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

        export function ensureLambdaFlow() {
          return sdk();
        }

        export function isLambdaFlowAvailable() {
          return Boolean(window.LambdaFlow && typeof window.send === 'function');
        }

        export function configureLambdaFlow(options) {
          return sdk().configure(options);
        }

        export function request(kind, payload = null, timeoutOrOptions = 30000) {
          return sdk().request(kind, payload, timeoutOrOptions);
        }

        export function requestEntity(kind, type, data, timeoutOrOptions = 30000, version = 1) {
          return sdk().requestEntity(kind, type, data, timeoutOrOptions, version);
        }

        export function send(kind, payload = null, options) {
          sdk().send(kind, payload, options);
        }

        export function sendEntity(kind, type, data, version = 1, options) {
          sdk().sendEntity(kind, type, data, version, options);
        }

        export function on(kind, handler, options) {
          return sdk().on(kind, handler, options);
        }

        export function onAny(handler, options) {
          return sdk().onAny(handler, options);
        }

        export function once(kind, handler, options) {
          return sdk().once(kind, handler, options);
        }

        export function handle(kind, handler, options) {
          return sdk().handle(kind, handler, options);
        }

        export function pendingCount() {
          return sdk().pendingCount();
        }

        export function createEntity(type, data, version = 1) {
          return sdk().entity(type, data, version);
        }

        export function unwrapEntity(payload) {
          return sdk().unwrapEntity(payload);
        }

        export async function pingBackend() {
          return request('backend.ping', null, 5000);
        }

        export const lf = {
          ensureAvailable: ensureLambdaFlow,
          isAvailable: isLambdaFlowAvailable,
          configure: configureLambdaFlow,
          request,
          requestEntity,
          send,
          sendEntity,
          emit: send,
          on,
          onAny,
          once,
          handle,
          pendingCount,
          entity: createEntity,
          unwrapEntity
        };
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
        CopyJavaScriptSdk(frameworkRoot, Path.Combine(frontendDir, "public", "lambdaflow.js"));
        CopyJavaScriptSdkTypes(frameworkRoot, Path.Combine(frontendDir, "src", "services", "lambdaflow.d.ts"));
        CopyJavaScriptSdkApi(frameworkRoot, Path.Combine(frontendDir, "src", "services", "lambdaflowApi.ts"));
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

        `src/services/lambdaflowApi.js` is used by the JavaScript template.
        `src/services/lambdaflowApi.ts` and `src/services/lambdaflow.d.ts`
        are included for TypeScript projects.
        """);
    }

    private static void CreateVueFrontend(string projectDir, string frameworkRoot) {
        var frontendDir = Path.Combine(projectDir, "frontend");

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "package.json"), """
        {
          "type": "module",
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "preview": "vite preview"
          },
          "dependencies": {
            "vue": "3.5.40"
          },
          "devDependencies": {
            "@vitejs/plugin-vue": "6.0.8",
            "vite": "8.1.5"
          }
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "vite.config.js"), """
        import { defineConfig } from 'vite';
        import vue from '@vitejs/plugin-vue';

        export default defineConfig({
          base: './',
          plugins: [vue()]
        });
        """);

        WriteViteIndex(frontendDir, "Vue", "/src/main.js");
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "main.js"), """
        import { createApp } from 'vue';
        import App from './App.vue';
        import './styles.css';

        createApp(App).mount('#app');
        """);
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "App.vue"), """
        <script setup>
        import { onMounted, ref } from 'vue';
        import { getHostStatus, pingBackend } from './services/lambdaflowApi.js';

        const hostStatus = getHostStatus();
        const result = ref('');
        const isPinging = ref(false);

        async function ping() {
          isPinging.value = true;
          result.value = '';
          try {
            const response = await pingBackend();
            result.value = typeof response === 'string' ? response : JSON.stringify(response);
          } catch (error) {
            result.value = error instanceof Error ? error.message : String(error);
          } finally {
            isPinging.value = false;
          }
        }

        onMounted(() => {
          if (hostStatus.available) void ping();
        });
        </script>

        <template>
          <main class="shell">
            <section class="panel">
              <p class="eyebrow">LambdaFlow</p>
              <h1>Vue frontend ready</h1>
              <p class="lead">The generated application verifies the backend connection on startup.</p>
              <div :class="['status', hostStatus.available ? 'ok' : 'warn']">
                {{ hostStatus.message }}
              </div>
              <button type="button" :disabled="!hostStatus.available || isPinging" @click="ping">
                {{ isPinging ? 'Pinging…' : 'Ping backend' }}
              </button>
              <pre v-if="result" class="result">{{ result }}</pre>
            </section>
          </main>
        </template>
        """);

        WriteViteFrontendSharedFiles(frontendDir, frameworkRoot, "Vue");
    }

    private static void CreateSvelteFrontend(string projectDir, string frameworkRoot) {
        var frontendDir = Path.Combine(projectDir, "frontend");

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "package.json"), """
        {
          "type": "module",
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "preview": "vite preview"
          },
          "dependencies": {
            "svelte": "5.56.7"
          },
          "devDependencies": {
            "@sveltejs/vite-plugin-svelte": "7.2.0",
            "vite": "8.1.5"
          }
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "vite.config.js"), """
        import { defineConfig } from 'vite';
        import { svelte } from '@sveltejs/vite-plugin-svelte';

        export default defineConfig({
          base: './',
          plugins: [svelte()]
        });
        """);
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "svelte.config.js"), """
        export default {};
        """);

        WriteViteIndex(frontendDir, "Svelte", "/src/main.js");
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "main.js"), """
        import { mount } from 'svelte';
        import App from './App.svelte';
        import './styles.css';

        mount(App, { target: document.getElementById('app') });
        """);
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "App.svelte"), """
        <script>
          import { onMount } from 'svelte';
          import { getHostStatus, pingBackend } from './services/lambdaflowApi.js';

          const hostStatus = getHostStatus();
          let result = '';
          let isPinging = false;

          async function ping() {
            isPinging = true;
            result = '';
            try {
              const response = await pingBackend();
              result = typeof response === 'string' ? response : JSON.stringify(response);
            } catch (error) {
              result = error instanceof Error ? error.message : String(error);
            } finally {
              isPinging = false;
            }
          }

          onMount(() => {
            if (hostStatus.available) void ping();
          });
        </script>

        <main class="shell">
          <section class="panel">
            <p class="eyebrow">LambdaFlow</p>
            <h1>Svelte frontend ready</h1>
            <p class="lead">The generated application verifies the backend connection on startup.</p>
            <div class:ok={hostStatus.available} class:warn={!hostStatus.available} class="status">
              {hostStatus.message}
            </div>
            <button type="button" disabled={!hostStatus.available || isPinging} on:click={ping}>
              {isPinging ? 'Pinging…' : 'Ping backend'}
            </button>
            {#if result}<pre class="result">{result}</pre>{/if}
          </section>
        </main>
        """);

        WriteViteFrontendSharedFiles(frontendDir, frameworkRoot, "Svelte");
    }

    private static void WriteViteIndex(string frontendDir, string name, string entrypoint) {
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "index.html"), $$"""
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>LambdaFlow {{name}} App</title>
            <script src="/lambdaflow.js"></script>
          </head>
          <body>
            <div id="app"></div>
            <script type="module" src="{{entrypoint}}"></script>
          </body>
        </html>
        """);
    }

    private static void WriteViteFrontendSharedFiles(
        string frontendDir,
        string frameworkRoot,
        string frameworkName) {
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "services", "lambdaflowApi.js"), """
        function sdk() {
          if (!window.LambdaFlow)
            throw new Error('LambdaFlow JavaScript SDK is not loaded.');
          window.LambdaFlow.ensureAvailable();
          return window.LambdaFlow;
        }

        export function getHostStatus() {
          const available = Boolean(window.LambdaFlow && typeof window.send === 'function');
          return {
            available,
            message: available
              ? 'LambdaFlow host detected.'
              : 'LambdaFlow host not detected. Run this frontend inside the packaged application.'
          };
        }

        export function request(kind, payload = null, timeoutOrOptions = 30000) {
          return sdk().request(kind, payload, timeoutOrOptions);
        }

        export function send(kind, payload = null, options) {
          sdk().send(kind, payload, options);
        }

        export function on(kind, handler, options) {
          return sdk().on(kind, handler, options);
        }

        export function handle(kind, handler, options) {
          return sdk().handle(kind, handler, options);
        }

        export function pingBackend() {
          return request('backend.ping', null, { timeoutMs: 5000 });
        }
        """);

        FileSystemTools.WriteFile(Path.Combine(frontendDir, "src", "styles.css"), """
        :root {
          color: #17202a;
          background: #f5f7fb;
          font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        }
        * { box-sizing: border-box; }
        body { margin: 0; }
        .shell { min-height: 100vh; display: grid; place-items: center; padding: 32px; }
        .panel {
          width: min(620px, 100%);
          border: 1px solid #d8dee9;
          border-radius: 8px;
          background: white;
          padding: 28px;
          box-shadow: 0 20px 50px rgba(15, 23, 42, 0.08);
        }
        .eyebrow { color: #1264a3; font-size: 12px; font-weight: 700; letter-spacing: .08em; margin: 0 0 8px; text-transform: uppercase; }
        h1 { font-size: 34px; line-height: 1.1; margin: 0; }
        .lead { color: #526070; line-height: 1.6; margin: 14px 0 22px; }
        .status { border-radius: 6px; margin-bottom: 16px; padding: 10px 12px; }
        .status.ok { background: #e8f5ee; color: #17633a; }
        .status.warn { background: #fff4df; color: #7a4d00; }
        button { border: 0; border-radius: 6px; background: #1264a3; color: white; cursor: pointer; font: inherit; padding: 10px 14px; }
        button:disabled { cursor: default; opacity: .55; }
        .result { background: #101820; border-radius: 6px; color: #f8fafc; margin: 16px 0 0; overflow: auto; padding: 12px; }
        """);

        CopyJavaScriptSdk(frameworkRoot, Path.Combine(frontendDir, "public", "lambdaflow.js"));
        CopyJavaScriptSdkTypes(frameworkRoot, Path.Combine(frontendDir, "src", "services", "lambdaflow.d.ts"));
        CopyJavaScriptSdkApi(frameworkRoot, Path.Combine(frontendDir, "src", "services", "lambdaflowApi.ts"));
        FileSystemTools.WriteFile(Path.Combine(frontendDir, "README.md"), $$"""
        # {{frameworkName}} frontend

        LambdaFlow runs `npm install` and `npm run build` before packaging. The
        generated app loads the canonical frontend SDK before the framework
        entrypoint and automatically sends `backend.ping` when the host opens.

        Use `src/services/lambdaflowApi.js` as the application-facing adapter.
        The canonical TypeScript declarations and adapter are included beside it.
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

    private static void CopyJavaScriptSdk(string frameworkRoot, string targetPath) {
        var sdkSource = Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "JavaScript", "lambdaflow.js");

        if (!File.Exists(sdkSource))
            throw new FileNotFoundException($"JavaScript SDK source file not found at '{sdkSource}'.");

        FileSystemTools.WriteFile(targetPath, File.ReadAllText(sdkSource));
    }

    private static void CopyJavaScriptSdkTypes(string frameworkRoot, string targetPath) {
        var sdkTypesSource = Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "JavaScript", "lambdaflow.d.ts");

        if (File.Exists(sdkTypesSource))
            FileSystemTools.WriteFile(targetPath, File.ReadAllText(sdkTypesSource));
    }

    private static void CopyJavaScriptSdkApi(string frameworkRoot, string targetPath) {
        var sdkApiSource = Path.Combine(frameworkRoot, "lambdaflow", "Sdk", "JavaScript", "lambdaflowApi.ts");

        if (File.Exists(sdkApiSource))
            FileSystemTools.WriteFile(targetPath, File.ReadAllText(sdkApiSource));
    }

    private static bool IsBuildArtifactDirectory(string part) {
        return part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("target", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Results", StringComparison.OrdinalIgnoreCase)
            || part.Equals("__pycache__", StringComparison.OrdinalIgnoreCase)
            || part.Equals(".pytest_cache", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCanonicalBackendSdk(ProjectLanguage language) {
        return language is ProjectLanguage.CSharp or ProjectLanguage.Java or ProjectLanguage.Python;
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
            case ProjectLanguage.Node:
            case ProjectLanguage.Go:
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

        var pomText = File.ReadAllText(pomPath)
            .Replace(
                "${project.basedir}/../../../lambdaflow/Sdk/Java",
                "${project.basedir}/../lambdaflow/Sdk/Java",
                StringComparison.Ordinal);
        if (pomText.Contains("build-helper-maven-plugin", StringComparison.Ordinal)) {
            File.WriteAllText(pomPath, pomText);
            return;
        }

        var plugin = """
            <plugin>
                <groupId>org.codehaus.mojo</groupId>
                <artifactId>build-helper-maven-plugin</artifactId>
                <version>3.6.1</version>
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
              "program": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/linux-x64/{{sanitized}}",
              "args": [],
              "cwd": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/linux-x64",
              "console": "internalConsole",
              "stopAtEntry": false,
              "windows": {
                "program": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/windows-x64/{{sanitized}}.exe",
                "cwd": "${workspaceFolder}/Results/{{sanitized}}-1.0.0/windows-x64"
              }
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

    private static void CreateProjectGitIgnore(string projectDir) {
        FileSystemTools.WriteFile(Path.Combine(projectDir, ".gitignore"), """
        # LambdaFlow packages and build output
        Results/
        bin/
        obj/
        target/
        frontend/dist/

        # Dependencies and caches
        node_modules/
        frontend/node_modules/
        __pycache__/
        *.py[cod]
        .pytest_cache/
        .gradle/

        # Local diagnostics and editor state
        *.log
        .DS_Store
        Thumbs.db
        """);
    }

    private static string SanitizeFileName(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars   = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }
}
