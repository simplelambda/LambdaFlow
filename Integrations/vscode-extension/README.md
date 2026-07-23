# LambdaFlow for VS Code

Create, configure, build, run, and debug Windows and Linux desktop applications
with web frontends and the backend language you choose—all without leaving VS
Code.

Current extension release: **1.3.1**.

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Setup](#setup)
- [Commands](#commands)
- [Project wizard](#project-wizard)
- [Configuration editor](#configuration-editor)
- [Build and run behavior](#build-and-run-behavior)
- [Security and workspace trust](#security-and-workspace-trust)
- [Extension development](#extension-development)

## Features

- Create self-contained LambdaFlow project source trees.
- Choose C#, Java, Python, Node.js, Go, or a custom backend.
- Choose basic HTML or a Vite-based React, Vue, or Svelte frontend.
- Edit `config.json` through a visual editor without discarding custom fields.
- Build, run, and debug the current Windows or Linux target.
- Locate user-installed .NET and Node.js toolchains across both platforms.
- Use the same CLI and package format as terminal-based LambdaFlow workflows.

## Requirements

- VS Code 1.85+
- .NET 8 SDK
- Git, when using automatic framework download
- Node.js and npm for React, Vue, and Svelte frontends or a Node.js backend
- A LambdaFlow source tree, configured manually or downloaded by the extension
- Backend toolchain selected by the project
- Linux runtime: GTK 3 and WebKitGTK 4.1
- Windows runtime: Microsoft Edge WebView2

## Setup

1. Install the extension and open the LambdaFlow sidebar from the λ activity-bar
   icon.
2. Run **LambdaFlow: New Project**, or open an existing LambdaFlow project.
3. When prompted, clone the framework to the recommended location, choose a
   parent folder for the clone, or select an existing repository.

If the framework is not configured, the recommended clone location is:

- Windows: `%APPDATA%/LambdaFlow/framework`
- Linux: `$XDG_DATA_HOME/LambdaFlow/framework` or `~/.local/share/LambdaFlow/framework`

Projects may be created anywhere, independently of that repository. A selected
custom parent receives a `LambdaFlow` subfolder. Downloads are staged in a
temporary sibling folder, so the extension never deletes a pre-existing invalid
target. A self-contained project's embedded CLI can build that project, while
**New Project** requires a complete repository containing the SDKs and examples.

The extension locates `dotnet` through `DOTNET_HOST_PATH`, `DOTNET_ROOT`, `PATH`,
standard platform locations and `~/.dotnet`. If the SDK is installed elsewhere,
set `LambdaFlow: Dotnet Path` to the absolute executable path. CLI tasks use direct
process execution, so they do not depend on Bash, PowerShell or Fish quoting.

For React, Vue, Svelte, and Node.js builds, the extension also locates Node.js through `NODE`, `PATH`,
standard install locations, `~/.local/share/node` and `~/.local/node`. It adds
the Node and .NET executable directories to the CLI task environment, so nested
`npm`, `npx` and `dotnet publish` commands work even when VS Code was launched
without those user-local directories in `PATH`. Set `LambdaFlow: Node Path` if
Node.js is installed elsewhere.

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
3. Backend template: C#, Java, Python, Node.js, Go, or Other
4. Frontend template: basic HTML, React, Vue, or Svelte

The extension invokes the LambdaFlow CLI and creates a self-contained source
project. Backend compile/runtime defaults for Windows and Linux are written to
`config.json`. C#, Java, and Python receive their canonical SDK; Node.js and Go
receive dependency-free protocol starters. Every Vite template includes an
automatic `backend.ping` connectivity check.

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
Windows x64   → Results/<name>-<version>/windows-x64/<name>.exe
Windows arm64 → Results/<name>-<version>/windows-arm64/<name>.exe
Linux x64     → Results/<name>-<version>/linux-x64/<name>
Linux arm64   → Results/<name>-<version>/linux-arm64/<name>
```

The configured `resultFolder` is respected. Windows executables receive `.exe`; Linux executables do not.

The CLI performs prebuild commands, backend compilation, host publication, frontend packaging, icon copying, and integrity generation.

## Security and workspace trust

LambdaFlow projects can define pre-build and backend compile commands. Review a
project's `config.json` before building it and only use projects you trust. The
extension declares untrusted and virtual workspaces as unsupported, so VS Code
does not enable build or execution features there.

Automatic framework download clones only
`https://github.com/simplelambda/LambdaFlow.git`. The extension does not collect
telemetry or send project contents to a remote service. Normal VS Code, Git,
toolchain, and dependency security practices still apply.

## Extension development

```bash
cd Integrations/vscode-extension
npm ci
npm run compile
npm run package
```

Open the extension folder in VS Code and press `F5` to start an Extension Development Host.

Source files are under `src/`. The runtime entrypoint uses compiled files under `out/`, so run `npm run compile` after every source change.
