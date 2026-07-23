# LambdaFlow

Build native desktop applications with a web frontend and the backend language you choose.

Current stable release: **1.3.1**.

LambdaFlow packages HTML, CSS, and JavaScript into a native desktop window, starts an arbitrary backend executable, and routes JSON messages between them. The backend can be C#, Python, Java, Rust, Go, C++, or any other process that can read and write line-delimited JSON.

- Windows host: WinForms + Microsoft WebView2.
- Linux host: GTK 3 + WebKitGTK 4.1 through Photino.NET.
- Frontend: standard web technology.
- Backend: any executable or interpreter.
- Tooling: cross-platform .NET CLI and VS Code extension.

## Table of contents

- [How LambdaFlow works](#how-lambdaflow-works)
- [Features](#features)
- [Release 1.3.1](#release-131)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Build targets](#build-targets)
- [Project layout](#project-layout)
- [Configuration](#configuration)
- [Frontend SDK](#frontend-sdk)
- [Backend SDKs](#backend-sdks)
- [Wire protocol](#wire-protocol)
- [VS Code extension](#vs-code-extension)
- [Security model](#security-model)
- [Linux support](#linux-support)
- [Testing Windows from Linux](#testing-windows-from-linux)
- [Developing LambdaFlow](#developing-lambdaflow)
- [Troubleshooting](#troubleshooting)
- [Current scope](#current-scope)
- [Security notice and disclaimer](#security-notice-and-disclaimer)

## How LambdaFlow works

```text
┌──────────────────────────────────────────────────────────────────────┐
│                       LambdaFlow native host                         │
├──────────────────────────────────┬───────────────────────────────────┤
│ Windows                          │ Linux                             │
│ WinForms · WebView2              │ GTK 3 · WebKitGTK · Photino.NET   │
├──────────────────────────────────┴───────────────────────────────────┤
│ Web frontend · HTML · CSS · JavaScript · LambdaFlow JS SDK           │
├──────────────────────────────────────────────────────────────────────┤
│ IPC · Named Pipe (Windows) · StdIO (Linux)                           │
└──────────────────────────────────┬───────────────────────────────────┘
                                   │ JSON envelopes
                                   ▼
                  ┌─────────────────────────────────┐
                  │ Backend process                 │
                  │ C# · Java · Python · Node · Go  │
                  └─────────────────────────────────┘
```

The user launches the packaged host. The host verifies the SHA-256 integrity manifest, opens the native window, starts the configured backend, and forwards messages in both directions. LambdaFlow does not embed an HTTP server and does not force the backend to use JavaScript.

## Features

- Backend-language independence.
- Native Windows and Linux windows using each operating system's webview stack.
- A plain JavaScript frontend SDK usable with HTML, React, Vue, Svelte, or other web tooling.
- Aligned C#, Java, and Python backend SDKs.
- Request/response, fire-and-forget events, error envelopes, and typed entity payloads.
- `config.json` for application, window, build, runtime, debug, platform, and architecture settings.
- C#, Java, Python, Node.js, Go, and generic project templates.
- Basic HTML and Vite-based React, Vue, and Svelte frontend templates.
- Cross-compilation of Windows artifacts from Linux with the .NET SDK.
- VS Code project creation, configuration editor, build, run, and debug commands.
- SHA-256 bundle verification and a restricted local frontend origin.

## Release 1.3.1

Version 1.3.1 is a backward-compatible patch over 1.3.0. It fixes generated C#
applications opening with a disconnected frontend when the backend cannot find a
global .NET runtime. Generated C# backends are now self-contained, and both hosts
report an early backend exit with its code and recent stderr.

This release also adds Node.js and Go backend starters, Vue and Svelte frontend
templates, automatic Vite-template connectivity checks, and safer framework
discovery and cloning in the VS Code extension. The wire protocol, configuration
format, and public SDK API remain compatible with 1.3.0.

## Requirements

### Common development requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git
- The compiler or runtime required by the selected backend:
  - C#: .NET 8 SDK (the generated backend is published self-contained)
  - Java: JDK 17+ and Maven
  - Python: Python 3.10+
  - Node.js backend: Node.js
  - Go backend: Go toolchain
  - React, Vue, and Svelte templates: Node.js and npm

The LambdaFlow host and generated C# backend are published self-contained. Java,
Python, and Node.js projects still require their selected runtime on the target
machine; Go produces a native executable.

### Windows runtime

- Windows 10 or Windows 11, x64 or arm64
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
  Windows 11 normally includes it.

### Linux runtime

- A glibc-based desktop distribution
- GTK 3
- WebKitGTK 4.1

Package names vary by distribution:

```bash
# Arch Linux / CachyOS
sudo pacman -S dotnet-sdk gtk3 webkit2gtk-4.1

# Debian / Ubuntu
sudo apt install dotnet-sdk-8.0 libgtk-3-0 libwebkit2gtk-4.1-0

# Fedora
sudo dnf install dotnet-sdk-8.0 gtk3 webkit2gtk4.1
```

Only runtime libraries are needed to launch a packaged app. Development packages are not required because the Linux native bridge is supplied by the Photino.NET package.

## Quick start

### 1. Create a project

From the LambdaFlow repository:

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  new MyApp Apps/MyApp \
  --framework . \
  --language csharp \
  --frontend basic
```

Supported backend templates:

- `csharp`
- `java`
- `python`
- `node`
- `go`
- `other`

Supported frontend templates:

- `basic`
- `react`
- `vue`
- `svelte`

Every generated Vite frontend sends `backend.ping` when it opens, making transport
or backend startup failures immediately visible. Add `--self-contained` to copy
the framework sources required by the generated project into that project.

### 2. Build for the current operating system

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp \
  --framework .
```

On Linux x64 the result is:

```text
Apps/MyApp/Results/MyApp-1.0.0/linux-x64/
```

On Windows x64 the result is:

```text
Apps/MyApp/Results/MyApp-1.0.0/windows-x64/
```

### 3. Run

Linux:

```bash
./Apps/MyApp/Results/MyApp-1.0.0/linux-x64/MyApp
```

Windows PowerShell:

```powershell
.\Apps\MyApp\Results\MyApp-1.0.0\windows-x64\MyApp.exe
```

## Build targets

If `--target` is omitted, the CLI selects the current OS and CPU architecture.

| Target | CLI value | Host | Output folder |
|---|---|---|---|
| Windows x64 | `windows-x64` | WebView2 | `windows-x64/` |
| Windows arm64 | `windows-arm64` | WebView2 | `windows-arm64/` |
| Linux x64 | `linux-x64` | GTK/WebKitGTK | `linux-x64/` |
| Linux arm64 | `linux-arm64` | GTK/WebKitGTK | `linux-arm64/` |

Examples:

```bash
# Native Linux build
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp --framework . --target linux-x64

# Cross-compile Windows from Linux
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build Apps/MyApp --framework . --target windows-x64
```

The target must exist under `platforms` in `config.json`. Cross-compilation proves that the Windows host and backend compile, but a Windows installation or VM is still required to exercise WebView2 and Windows named-pipe behavior.

## Project layout

Generated source project:

```text
MyApp/
├── config.json
├── backend/
├── frontend/
├── lambdaflow/
│   └── Sdk/
│       └── <selected backend SDK>
├── .vscode/
│   ├── launch.json
│   ├── settings.json
│   └── tasks.json
└── Results/                 generated
```

Packaged application:

```text
Results/MyApp-1.0.0/linux-x64/
├── MyApp                    MyApp.exe on Windows
├── config.json
├── frontend.pak
├── lambdaflow.integrity.json
├── backend/
│   └── backend runtime files
└── host runtime files
```

The generator copies the canonical backend SDK for C#, Java, or Python. Node.js
and Go receive dependency-free protocol starters, while `other` receives an
editable generic skeleton. Every frontend receives `lambdaflow.js`.

## Configuration

`config.json` is the source of truth for development and packaged runtime settings.

```json
{
  "appName": "MyApp",
  "appVersion": "1.0.0",
  "organizationName": "MyCompany",
  "appIcon": "app.ico",
  "securityMode": "Hardened",
  "ipcTransport": "Auto",
  "developmentBackendFolder": "backend",
  "developmentFrontendFolder": "frontend",
  "resultFolder": "Results",
  "frontendInitialHTML": "index.html",
  "build": {
    "preBuild": [
      {
        "name": "Build frontend",
        "command": "npm run build",
        "workingDirectory": "frontend",
        "enabled": true,
        "continueOnError": false,
        "timeoutSeconds": 120
      }
    ]
  },
  "debug": {
    "enabled": false,
    "frontendDevTools": false,
    "openFrontendDevToolsOnStart": false,
    "captureFrontendConsole": false,
    "showBackendConsole": false,
    "backendLogLevel": "info"
  },
  "platforms": {
    "windows": {
      "archs": {
        "x64": {
          "compileCommand": "dotnet publish Backend.csproj -c Release -r win-x64 --self-contained true -o bin/win-x64",
          "compileDirectory": "bin/win-x64",
          "runCommand": "Backend.exe",
          "runArgs": []
        }
      }
    },
    "linux": {
      "archs": {
        "x64": {
          "compileCommand": "dotnet publish Backend.csproj -c Release -r linux-x64 --self-contained true -o bin/linux-x64",
          "compileDirectory": "bin/linux-x64",
          "runCommand": "Backend",
          "runArgs": []
        }
      }
    }
  },
  "window": {
    "title": "My App",
    "width": 1000,
    "height": 700,
    "minWidth": 640,
    "minHeight": 480,
    "maxWidth": 0,
    "maxHeight": 0
  }
}
```

Important rules:

- `ipcTransport: "Auto"` selects named pipes on Windows and StdIO on Linux.
- An older config containing `NamedPipe` is automatically treated as StdIO on Linux.
- With StdIO, the backend must reserve stdout for protocol messages and write logs to stderr.
- `compileCommand` runs inside `developmentBackendFolder`.
- `compileDirectory` is relative to that backend folder and is copied into the package.
- Use a distinct `compileDirectory` per native target (as generated for C#) so a package cannot inherit binaries from a previous target.
- `runCommand` is resolved inside the packaged `backend/` directory first, then through `PATH`.
- `runArgs` is an argument array; no shell parsing is applied at runtime.
- Prebuild, backend, frontend, and result paths must remain inside the project.
- `maxWidth` and `maxHeight` set to `0` mean no maximum.
- Prebuild commands run on the machine performing the build. Use portable commands when the same project is built on multiple operating systems.

## Frontend SDK

Load the SDK before application scripts:

```html
<script src="lambdaflow.js"></script>
<script src="app.js"></script>
```

The host exposes the low-level functions `window.send(rawJson)` and `window.receive(rawJson)`. Application code should use `window.LambdaFlow`.

### Requests

```js
const result = await LambdaFlow.request(
  'uppercase',
  { text: 'Hello' },
  { timeoutMs: 5000 }
);
```

`request` correlates responses by `id`, supports timeouts and `AbortSignal`, and rejects with `LambdaFlow.Error` when the backend returns `ok: false`.

### Events

```js
LambdaFlow.send('telemetry.clicked', { button: 'save' });

const unsubscribe = LambdaFlow.on('backend.progress', progress => {
  console.log(progress);
});

LambdaFlow.once('backend.ready', payload => {
  console.log(payload);
});

unsubscribe();
```

`emit` is an alias for `send`; `receive` is an alias for `on`. `onAny` subscribes to all event kinds.

### Frontend request handlers

Backends can request information from the frontend:

```js
const unregister = LambdaFlow.handle('ui.getTheme', async () => {
  return { theme: document.documentElement.dataset.theme };
});
```

The SDK sends a correlated success or error response automatically. Use `unhandle(kind)` to remove the handler.

### Entities

```js
const dog = LambdaFlow.entity('animals.dog', {
  name: 'Rex',
  age: 4
});

await LambdaFlow.requestEntity('describeDog', 'animals.dog', dog.data);
```

Entity shape:

```json
{
  "$type": "animals.dog",
  "$v": 1,
  "data": {}
}
```

Entities are unwrapped by default before delivery to handlers. Metadata still exposes the type, version, raw payload, envelope, and receive time.

### Complete public frontend API

```text
version
configure
isHostAvailable / isAvailable
ensureHostAvailable / ensureAvailable
send / emit / sendEnvelope
request / requestEntity
on / receive / onAny / once / off
handle / unhandle
respond / reject
entity / sendEntity
isEntity / unwrapEntity / entityType / entityVersion
receiveRaw
pendingCount / clearHandlers / destroy
```

TypeScript declarations are in `lambdaflow/Sdk/JavaScript/lambdaflow.d.ts`. The optional `lambdaflowApi.ts` module exposes the same operations as importable functions.

## Backend SDKs

Canonical SDK files:

| Language | File |
|---|---|
| C# | `lambdaflow/Sdk/CSharp/LambdaFlow.cs` |
| Java | `lambdaflow/Sdk/Java/LambdaFlow.java` |
| Python | `lambdaflow/Sdk/Python/lambdaflow.py` |

The APIs use each language's naming conventions but share the same concepts:

| Concept | C# | Java | Python |
|---|---|---|---|
| SDK version | `Version` | `VERSION` | `__version__`, `VERSION` |
| Configure | `Configure` | `configure` | `configure` |
| Register event/request | `Receive`, `On`, `Handle` | `receive`, `on`, `handle` | `receive`, `on`, `handle` |
| Remove handler | `Unhandle`, `Off` | `unhandle`, `off` | `unhandle`, `off` |
| Send event | `Send`, `Emit` | `send`, `emit` | `send`, `emit` |
| Request frontend | `Request`, `RequestAsync` | `request`, `requestAsync` | `request` |
| Manual response | `Respond`, `Reject` | `respond`, `reject` | `respond`, `reject` |
| Entities | `Entity`, `SendEntity`, `RequestEntityAsync` | `entity`, `sendEntity`, `requestEntityAsync` | `entity`, `send_entity`, `request_entity` |
| Run loop | `Run`, `RunAsync`, `Stop` | `run`, `stop` | `run`, `stop` |
| Pending requests | `PendingCount` | `pendingCount` | `pending_count` |

C#:

```csharp
LambdaFlow.Receive<TextRequest, TextResponse>(
    "uppercase",
    request => new(request.Text.ToUpperInvariant()));

LambdaFlow.Run();
```

Python:

```python
import lambdaflow as lf

@lf.handle("uppercase")
def uppercase(request):
    return {"text": request["text"].upper()}

lf.run()
```

Java:

```java
LambdaFlow.handle(
    "uppercase",
    TextRequest.class,
    request -> new TextResponse(request.text.toUpperCase()));

LambdaFlow.run();
```

Best practices:

- Register handlers before starting the run loop.
- Return a value from a handler instead of manually responding.
- Throw/raise an error to produce an `ok: false` response.
- Use `send` for events and `request` only when a response is required.
- Use entity payloads only when type identity or schema versioning adds value.
- Put diagnostics on stderr when using StdIO.
- Keep handlers independent of the transport; SDKs choose it from environment variables.

## Wire protocol

One UTF-8 JSON object is sent per line.

Request:

```json
{
  "kind": "uppercase",
  "id": "9d42...",
  "payload": { "text": "hello" }
}
```

Success:

```json
{
  "kind": "uppercase.result",
  "id": "9d42...",
  "ok": true,
  "payload": { "text": "HELLO" }
}
```

Failure:

```json
{
  "kind": "uppercase.result",
  "id": "9d42...",
  "ok": false,
  "error": {
    "code": "INVALID_INPUT",
    "message": "text is required",
    "details": {}
  }
}
```

Rules:

- `kind` is a required non-empty routing key.
- `id` is present when a response is expected.
- Responses reuse the same `id`.
- SDK-generated response kinds append `.result`.
- `ok: false` and `error` represent a failed request.
- Event envelopes may omit `id` and `ok`.
- New integrations should use the top-level `error` object. SDKs still accept the legacy `payload.error` shape.

Transport environment:

```text
LAMBDAFLOW_IPC_TRANSPORT=NamedPipe
LAMBDAFLOW_PIPE_NAME=<private-name>
```

If these variables are absent, SDKs use stdin/stdout.

## VS Code extension

The extension is under `Integrations/vscode-extension`.

Commands:

- `LambdaFlow: New Project`
- `LambdaFlow: Build`
- `LambdaFlow: Build & Run`
- `LambdaFlow: Build & Debug`
- `LambdaFlow: Edit Configuration`

It works on Windows and Linux. Build and run select the current OS/architecture, locate the correct output folder, and launch `.exe` only on Windows.

CLI tasks use direct process execution instead of shell command strings, so they
work with Bash, Fish, PowerShell and other configured terminals. The extension
looks for the .NET SDK through `DOTNET_HOST_PATH`, `DOTNET_ROOT`, `PATH`, standard
installation directories and `~/.dotnet`. Use `LambdaFlow: Dotnet Path` when the
SDK executable is installed elsewhere. A missing SDK is reported before a task is
started instead of surfacing as shell exit code 127.

For React, Vue, Svelte, and Node.js projects, it also locates Node.js in `NODE`, `PATH`, standard locations,
`~/.local/share/node` and `~/.local/node`. The .NET and Node directories are added
to the CLI task environment so nested `dotnet`, `npm` and `npx` commands resolve.
Use `LambdaFlow: Node Path` for a custom installation.

The project may live anywhere; it does not need to be inside the LambdaFlow
repository. If no valid framework source is configured, the extension can clone
the public repository to its recommended application-data location, clone it
under a parent folder chosen by the user, or select an existing checkout. Cloning
uses a temporary sibling directory and never deletes a pre-existing invalid
target. An embedded CLI inside a self-contained application project is sufficient
to build that project, but it is not mistaken for the complete template
repository when creating another project.

The configuration editor supports:

- App metadata and icon
- Window size limits
- Frontend/backend folders
- Ordered prebuild commands
- Windows x64 compile and runtime settings
- Linux x64 compile and runtime settings
- Auto, NamedPipe, and StdIO transport selection
- Debug and frontend console capture settings

Development:

```bash
cd Integrations/vscode-extension
npm install
npm run compile
```

Press `F5` in VS Code with the extension folder selected to open an Extension Development Host.

## Security model

LambdaFlow currently supports only `securityMode: "Hardened"`.

- The CLI writes `lambdaflow.integrity.json` with SHA-256 hashes for all packaged files.
- The host refuses to start if a listed file is missing or modified.
- Frontend files are served from a private local origin, not directly from arbitrary filesystem URLs.
- Path traversal outside `frontend.pak` is rejected.
- The frontend receives a restrictive Content Security Policy.
- Windows disables host objects, context menus, browser shortcuts, status UI, and DevTools unless debug settings allow them.
- Linux disables context menus and DevTools unless debug settings allow them.
- Windows named pipes are private to the current user.

The integrity manifest detects accidental or post-build modification; it is not a publisher signature. An attacker who can replace both application files and the manifest can recalculate hashes. Add platform code signing for release authenticity.

## Linux support

LambdaFlow uses one Linux implementation rather than separate Debian, Arch, and Red Hat code paths.

The managed host and native Photino bridge are the same across distributions for a given CPU architecture. Distribution-specific work is limited to installing GTK 3 and WebKitGTK 4.1 packages.

Supported Linux characteristics:

- x64 and arm64 publish targets
- X11 and Wayland environments supported by GTK/WebKitGTK
- glibc-based distributions
- StdIO backend transport
- Same `frontend.pak`, config, integrity, JavaScript API, and SDK protocol as Windows

Linux does not use WebView2 or Windows named pipes. `ipcTransport: "Auto"` accounts for this difference.

## Testing Windows from Linux

Three levels of verification are useful:

1. Cross-build on Linux:

   ```bash
   dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
     build Apps/MyApp --framework . --target windows-x64
   ```

   This validates C# compilation, NuGet resolution, backend target output, packaging, and integrity generation.

2. Automated protocol tests on Linux:

   Test the SDK and backend logic over StdIO without a GUI.

3. Functional Windows VM:

   Use KVM/QEMU with libvirt and virt-manager on CachyOS:

   ```bash
   sudo pacman -S qemu-full libvirt virt-manager edk2-ovmf swtpm dnsmasq
   sudo systemctl enable --now libvirtd
   ```

   Create a Windows 11 VM, install WebView2 if it is absent, share or copy the `windows-x64` output, and run the packaged executable.

KVM is the Linux equivalent appropriate for this test. Windows Sandbox is lightweight but runs only on a Windows host. Wine is useful for some Win32 programs but is not a faithful validation environment for WebView2, WinForms, and Windows named-pipe integration.

For repeatable CI, keep a Windows VM snapshot or add a native Windows CI runner.

## Developing LambdaFlow

Repository map:

```text
lambdaflow/
├── Core/                  shared config, backend process, integrity, interfaces
├── Hosts/
│   ├── Windows/           WinForms + WebView2 + named pipe/StdIO
│   └── Linux/             Photino + GTK/WebKitGTK + StdIO
├── Sdk/
│   ├── CSharp/
│   ├── Java/
│   ├── JavaScript/
│   └── Python/
├── Tools/LambdaFlow.Cli/  new/build commands
└── Ontology/              entity schema

Integrations/
├── vscode-extension/      extension source and compiled output
└── vscode/                repository task/launch templates

Examples/
├── CSharp/
├── Go/
├── Java/
├── Node/
└── Python/
```

Build checks:

```bash
dotnet build lambdaflow/Tools/LambdaFlow.Cli/LambdaFlow.Cli.csproj -c Release
dotnet build lambdaflow/Hosts/Linux/lambdaflow.linux.csproj -c Release
dotnet build lambdaflow/Hosts/Windows/lambdaflow.windows.csproj -c Release

cd Integrations/vscode-extension
npm install
npm run compile
```

Protocol smoke test:

```bash
printf '%s\n' \
  '{"kind":"uppercase","id":"smoke-1","payload":{"text":"hello"}}' \
  | ./backend-command
```

Expected logical response:

```json
{"kind":"uppercase.result","id":"smoke-1","ok":true,"payload":{"text":"HELLO"}}
```

Read `AGENTS.md` before making agent-assisted changes. It contains the minimal architecture, public APIs, invariants, and task-specific file map.

## Troubleshooting

### Linux reports that WebKitGTK is missing

Install GTK 3 and WebKitGTK 4.1 using the distribution package manager. Verify:

```bash
pkg-config --modversion gtk+-3.0
pkg-config --modversion webkit2gtk-4.1
```

### A C# backend says that .NET is missing

Projects generated by LambdaFlow 1.3.1 publish C# backends self-contained. For a
project generated by an older version, change each C# compile command to
`--self-contained true` and rebuild. The SDK is still required on the development
machine, but the packaged backend no longer depends on a global runtime.

### The backend exits immediately on Linux

Check that `platforms.linux.archs.<arch>.runCommand` matches the packaged filename and that it is executable.

### Protocol JSON appears in logs or logs break requests

With StdIO, stdout is the protocol channel. Write logs to stderr.

### Integrity verification fails

Do not edit files inside `Results/` after build. Rebuild so the CLI regenerates the manifest.

### Frontend requests time out

- Confirm the backend run command starts.
- Read the host startup error: LambdaFlow now includes the backend exit code and
  recent stderr when the process terminates during startup.
- Confirm a handler is registered for the exact `kind`.
- Check `lambdaflow.crash.log`.
- In debug mode, inspect `lambdaflow.frontend.log`.
- Make sure no backend logger writes to stdout under StdIO.

### Windows build works but the app does not open

Test on Windows, confirm WebView2 Runtime is installed, and inspect `lambdaflow.crash.log`. A successful Linux cross-build does not execute the Windows GUI stack.

## Current scope

- Supported desktop hosts: Windows and Linux.
- Supported host architectures: x64 and arm64.
- macOS is represented in the shared platform model but does not yet have a host implementation.
- Scaffolded backend languages: C#, Java, Python, Node.js, Go, and generic.
- Scaffolded frontend types: basic HTML, React, Vue, and Svelte.

Contributions should preserve the line-delimited JSON protocol and keep frontend application code independent of the host implementation.

## Security notice and disclaimer

LambdaFlow executes the backend command defined by each application and renders frontend code supplied by that application. Only build or run projects, dependencies, and packaged bundles that you trust. Do not place credentials or private keys in frontend assets, configuration files, logs, or source control.

The SHA-256 integrity manifest detects changes inside a built bundle, but it is not a digital signature and does not prove who published that bundle. Release distributors should additionally use the platform's code-signing mechanism, keep .NET, WebView2, GTK/WebKitGTK, Photino, backend runtimes, and application dependencies updated, and review their own IPC handlers and Content Security Policy requirements.

To report a suspected vulnerability, follow [SECURITY.md](SECURITY.md) and avoid publishing exploitable details in a public issue.

LambdaFlow is provided **as is**, without warranties or guarantees of security, availability, fitness for a particular purpose, or absence of defects. To the maximum extent permitted by applicable law, the authors and maintainers are not liable for claims, damages, data loss, security incidents, service interruption, or other liability arising from use, misuse, modification, or distribution of the software. Users and distributors are responsible for evaluating whether LambdaFlow is appropriate for their threat model and legal or regulatory requirements.
