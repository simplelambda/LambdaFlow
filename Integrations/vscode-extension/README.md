# LambdaFlow for VS Code

Create, configure, build, run, and debug LambdaFlow desktop applications on Windows and Linux without leaving VS Code.

## Contents

- [Requirements](#requirements)
- [Setup](#setup)
- [Commands](#commands)
- [Project wizard](#project-wizard)
- [Configuration editor](#configuration-editor)
- [Build and run behavior](#build-and-run-behavior)
- [Extension development](#extension-development)

## Requirements

- VS Code 1.85+
- .NET 8 SDK
- LambdaFlow source tree, selected through `lambdaflow.frameworkPath`
- Backend toolchain selected by the project
- Linux runtime: GTK 3 and WebKitGTK 4.1
- Windows runtime: Microsoft Edge WebView2

## Setup

1. Clone LambdaFlow.
2. Open VS Code Settings and search for `LambdaFlow`.
3. Set `LambdaFlow: Framework Path` to the repository root.
4. Open the LambdaFlow sidebar from the λ activity-bar icon.

If the framework is not configured, the extension can clone it under the platform user-data directory:

- Windows: `%APPDATA%/LambdaFlow/framework`
- Linux: `$XDG_DATA_HOME/LambdaFlow/framework` or `~/.local/share/LambdaFlow/framework`

## Commands

| Command | Purpose |
|---|---|
| `LambdaFlow: New Project` | Scaffold a backend/frontend project |
| `LambdaFlow: Build` | Build for the current OS and architecture |
| `LambdaFlow: Build & Run` | Build and launch the packaged host |
| `LambdaFlow: Build & Debug` | Force debug config for the package and launch it |
| `LambdaFlow: Edit Configuration` | Open the visual `config.json` editor |

The sidebar exposes the same actions and displays the open project's name, version, and IPC transport.

## Project wizard

The wizard asks for:

1. Application name
2. Target directory
3. Backend template: C#, Java, Python, or Other
4. Frontend template: basic HTML or React

The extension invokes the LambdaFlow CLI and creates a self-contained source project. Backend compile/runtime defaults for Windows and Linux are written to `config.json`; only the selected backend SDK is copied.

## Configuration editor

The visual editor preserves unknown JSON fields and supports:

- App name, version, organization, and icon
- Window title and min/initial/max sizes
- Backend/frontend/result folders
- Ordered prebuild commands
- Windows x64 compile directory, compile command, run command, and run arguments
- Linux x64 compile directory, compile command, run command, and run arguments
- `Auto`, `NamedPipe`, or `StdIO` transport
- DevTools, console capture, backend console, and debug log level

Use **View JSON** for architecture entries or custom fields not represented by the form.

## Build and run behavior

The extension delegates build logic to the CLI. It does not duplicate packaging behavior.

The current platform determines the output:

```text
Windows x64 → Results/<name>-<version>/windows-x64/<name>.exe
Linux x64   → Results/<name>-<version>/linux-x64/<name>
```

The configured `resultFolder` is respected. Windows executables receive `.exe`; Linux executables do not.

The CLI performs prebuild commands, backend compilation, host publication, frontend packaging, icon copying, and integrity generation.

## Extension development

```bash
cd Integrations/vscode-extension
npm install
npm run compile
```

Open the extension folder in VS Code and press `F5` to start an Extension Development Host.

Source files are under `src/`. The runtime entrypoint uses compiled files under `out/`, so run `npm run compile` after every source change.
