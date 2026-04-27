# LambdaFlow

**Build desktop apps with a web UI and the backend language you actually want.**

Most web-based desktop frameworks couple the UI to JavaScript on both sides. LambdaFlow does not. It gives you a proper native window with a full browser surface, then lets the backend be whatever executable you compile — C#, Python, Java, Go, Rust, or anything else. The two sides talk through a lightweight message bridge managed by a small C# host.

```
┌─────────────────────────────────────────────────────────┐
│                    LambdaFlow Host (.NET)                │
│                                                         │
│   ┌───────────────────┐       ┌──────────────────────┐  │
│   │   WebView2 (Win)  │◄─────►│   IPC Bridge         │  │
│   │   HTML / CSS / JS │       │   Named Pipe          │  │
│   │   frontend.pak    │       │   (stdio fallback)    │  │
│   └───────────────────┘       └──────────┬───────────┘  │
│                                          │               │
└──────────────────────────────────────────┼───────────────┘
                                           │
                              ┌────────────▼────────────┐
                              │   Backend executable    │
                              │   C# · Python · Java    │
                              │   Go · Rust · anything  │
                              └─────────────────────────┘
```

The user launches one executable — the LambdaFlow host. The host verifies the application bundle, opens the window, starts the backend, and routes messages between the two. The backend just reads lines and writes lines.

---

## Why not Electron or Tauri?

| | Electron | Tauri | LambdaFlow |
|---|---|---|---|
| UI technology | Web | Web | Web |
| Backend language | Node.js only | Rust only | Any executable |
| Runtime bundled | Chromium + Node | OS WebView | OS WebView |
| Bundle size | Large | Small | Small |
| Backend freedom | No | No | Yes |

LambdaFlow is the right tool when the backend needs to be something specific — a Python ML stack, a Java service, a compiled C# library — and you want the UI to stay web-based without rewriting business logic in JavaScript.

---

## Features

- **Language-agnostic backend** — compile your backend in any language; LambdaFlow runs the resulting executable.
- **Language-aware scaffolding** — `lambdaflow new` can scaffold C#, Java, or Python templates and copies only the selected language example.
- **Web frontend** — standard HTML, CSS, and JavaScript served from a secure local origin with a strict Content Security Policy.
- **Native window** — proper OS window via WinForms + WebView2, with title, size, min/max, and icon configured from `config.json`.
- **Named Pipe IPC** — fast, full-duplex, per-run private pipe on Windows; stdin/stdout fallback for portability.
- **Unified SDK ergonomics** — C#, Java, and Python SDKs expose the same core concepts (`receive`/`send`/`run`).
- **Editable build defaults at creation time** — choose language defaults for backend compile command and output directory, then customize them in the wizard.
- **Integrity verification** — SHA-256 manifest checked before the app starts; tampered bundles are refused.
- **Single config** — one `config.json` describes the app name, window, build commands, and platform targets.
- **CLI tooling** — `lambdaflow new` scaffolds a project; `lambdaflow build` compiles, packages, and signs the bundle.
- **VS Code integration** — run and debug with F5, no manual steps.

---

## Requirements

- Windows (Linux and macOS hosts are planned)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (installed on Windows 11 by default)

---

## Quick Start

**1. Create a new project**

```powershell
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- new MyApp Apps/MyApp --framework . --language csharp
```

This generates a complete project at `Apps/MyApp/` with a working backend, frontend, and VS Code configuration.

Supported template languages:

- `--language csharp`
- `--language java`
- `--language python`

Optional creation-time overrides:

- `--backend-compile-command "..."`
- `--backend-compile-directory "..."`

**2. Build the app**

```powershell
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- build Apps/MyApp --framework .
```

Output is written to:

```
Apps/MyApp/Results/MyApp-1.0.0/windows-x64/
```

**3. Run it**

```powershell
.\Apps\MyApp\Results\MyApp-1.0.0\windows-x64\lambdaflow.windows.exe
```

---

## Scaffold Language Templates

`lambdaflow new` scaffolds from the `Examples/<Language>` template and copies only that language's backend and frontend.

| Language | CLI value | Backend template source | Default compile command | Default compile directory | Runtime launch (`runCommand` + `runArgs`) |
|---|---|---|---|---|---|
| C# | `csharp` | `Examples/CSharp/backend` | `dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin` | `bin` | `Backend.exe` + `[]` |
| Java | `java` | `Examples/Java/backend` | `mvn -q -DskipTests package` | `target` | `java` + `[-jar, Backend.jar]` |
| Python | `python` | `Examples/Python/backend` | `python build.py` | `bin` | `python` + `[backend.py]` |

Notes:

- Build artifacts (`bin`, `obj`, `target`, `Results`, `__pycache__`) are excluded when copying templates.
- The generated `config.json` is pre-filled with compile/runtime defaults for the selected language.
- The scaffold stores the selected language SDK helper under `lambdaflow/Sdk/<Language>/` and wires the backend template to use that shared project-level location.

---

## Multi-Language Examples

Three complete examples are included and kept feature-aligned:

- `Examples/CSharp`
- `Examples/Java`
- `Examples/Python`

All three share the same frontend and expose the same backend tools, including:

- text transforms (`uppercase`, `lowercase`, `reverse`)
- counters (`charcount`, `wordcount`)
- number converter (`numberconverter`)
- extended analyzer (`textstats`)
- typed-object demo (`describeDog`) using the ontology entity format

---

## VS Code

The generated project includes `.vscode/tasks.json` and `.vscode/launch.json`. Open the project folder in VS Code and:

- **Build**: `Ctrl+Shift+B` → `LambdaFlow: build app`
- **Run and debug**: `F5` — builds the app and launches it with the debugger attached to the host.

To create a new project from VS Code (without a terminal), open the LambdaFlow repository folder, then run **LambdaFlow: New Project** from the sidebar or Command Palette.

The wizard asks for:

1. Application name
2. Backend template language (`C#`, `Java`, `Python`)
3. Target directory
4. Backend compile command (pre-filled by language, editable)
5. Backend compile output directory (pre-filled by language, editable)

After generation, you can still edit everything in `config.json` with **LambdaFlow: Edit Configuration**.

---

## Project Layout

After `lambdaflow new`, your project looks like this:

```
MyApp/
  config.json          App metadata, window settings, build commands
  backend/
    ...                Language template files (C#, Java, or Python)
  frontend/
    ...                Template frontend copied from selected example
  lambdaflow/
    Sdk/
      ...              Only the selected language helper is copied here
  .vscode/
    tasks.json         Build task
    launch.json        F5 launch config
  Results/             Build output (generated, not committed)
```

Backend template details by language:

- C#: `Backend.csproj`, `Program.cs` (SDK reference points to `../lambdaflow/Sdk/CSharp/LambdaFlow.cs`)
- Java: `pom.xml`, `src/main/java/example/Backend.java` (Maven adds `../lambdaflow/Sdk/Java` as a source folder)
- Python: `backend.py`, `build.py` (runtime import path points to `../lambdaflow/Sdk/Python/lambdaflow.py`)

The built app looks like this:

```
Results/MyApp-1.0.0/windows-x64/
  lambdaflow.windows.exe   The host you distribute
  lambdaflow.integrity.json
  frontend.pak             ZIP of the frontend folder
  config.json
  backend/
    Backend.exe            Your compiled backend
```

---

## Using Non-Native Languages

If your backend language is not scaffolded natively (for example C, C++, or Go), use this generic workflow:

1. Scaffold a project with any template to get the host, frontend, VS Code files, and `config.json`.
2. Replace the `backend/` folder contents with your own source/build files.
3. Set backend build/runtime settings in `config.json`:

```json
"platforms": {
  "windows": {
    "archs": {
      "x64": {
        "compileCommand": "<your build command>",
        "compileDirectory": "<folder that contains runtime backend files>",
        "runCommand": "<executable or interpreter>",
        "runArgs": ["<arg1>", "<arg2>"]
      }
    }
  }
}
```

4. Implement the LambdaFlow protocol in your backend process:
   - read one JSON message per line,
   - handle by `kind`,
   - write one JSON response per line,
   - respect `id` for request/response correlation.
5. Support transport selection:
   - `NamedPipe` when `LAMBDAFLOW_IPC_TRANSPORT=NamedPipe` and `LAMBDAFLOW_PIPE_NAME` is provided,
   - stdin/stdout fallback otherwise.

You do not need a language-specific LambdaFlow SDK to integrate. The line-based JSON protocol is the contract.

---

## Frontend API

LambdaFlow injects two low-level bridge functions into the browser context:

- `send(string)`
- `window.receive(string)`

For day-to-day development, use the provided `lambdaflow.js` helper instead of writing raw JSON plumbing.

```html
<script src="lambdaflow.js"></script>
<script>
  // Fire-and-forget
  LambdaFlow.send("greet", { name: "world" });

  // Request/response
  const res = await LambdaFlow.request("uppercase", { text: "hello" });
  console.log(res.text);

  // Ontology entity payload
  const dog = { name: "Rex", age: 4, breed: "Labrador" };
  const reply = await LambdaFlow.requestEntity("describeDog", "animals.dog", dog);
  console.log(reply);
</script>
```

If you prefer handling inbound events directly, register handlers with:

```js
LambdaFlow.receive("eventName", payload => {
  console.log(payload);
});
```

---

## Backend Protocol

The backend is a normal executable. It reads one message per line from the active transport and writes one response per line back.

Message envelope (JSON Lines):

```json
{ "kind": "<routing-key>", "id": "<uuid|null>", "payload": <any-json-or-entity|null> }
```

Behavior:

- If `id` is present, the message is a request and the backend should always answer once.
- If `id` is absent, the message is an event; answering is optional.

Recommended backend SDK methods (aligned across languages):

- `receive(kind, handler)` / `Receive(kind, handler)`
- `send(kind, payload)` / `Send(kind, payload)`
- `run()` / `Run()`

Backward-compatible aliases (`on` / `On`) are still available.

### Named Pipe (default on Windows)

The host sets two environment variables before starting the backend:

```
LAMBDAFLOW_IPC_TRANSPORT=NamedPipe
LAMBDAFLOW_PIPE_NAME=lambdaflow-<random-guid>
```

Connect to the pipe, then read and write line-terminated strings:

```csharp
// C# example
using var pipe   = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
using var reader = new StreamReader(pipe, Encoding.UTF8);
using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };

await pipe.ConnectAsync(10_000);

string? line;
while ((line = await reader.ReadLineAsync()) is not null) {
    var req  = JsonSerializer.Deserialize<Request>(line);
    var resp = Handle(req);
    await writer.WriteLineAsync(JsonSerializer.Serialize(resp));
}
```

### stdin / stdout fallback

If `LAMBDAFLOW_IPC_TRANSPORT` is not `NamedPipe`, read from `Console.In` and write to `Console.Out`:

```csharp
while (Console.ReadLine() is { } line) {
    var req  = JsonSerializer.Deserialize<Request>(line);
    var resp = Handle(req);
    Console.WriteLine(JsonSerializer.Serialize(resp));
}
```

Any language that can connect to a Windows named pipe or read/write stdin/stdout can be the backend.

---

## Universal Ontology (Entity v1)

LambdaFlow supports an optional payload convention for strongly-typed object exchange across languages:

```json
{
  "$type": "animals.dog",
  "$v": 1,
  "data": {
    "name": "Rex",
    "age": 4,
    "breed": "Labrador"
  }
}
```

Notes:

- Backends can send entity payloads with `SendEntity(...)` / `sendEntity(...)` / `send_entity(...)`.
- Frontends can build entity payloads with `LambdaFlow.entity(...)` and `LambdaFlow.requestEntity(...)`.
- C#, Java, and Python SDKs auto-unwrap entity payloads before deserializing request objects, so handlers stay clean.

Formal schema:

- `lambdaflow/Ontology/lambdaflow.ontology.v1.schema.json`

---

## Roadmap

- Signed integrity manifests (private key at build time, public key embedded in host)
- Installer generation (MSIX / WiX)
- Linux host (WebKitGTK + Unix domain sockets)
- macOS host (WKWebView + Unix domain sockets)
- Higher-level backend SDKs for additional languages (Go, Rust)

---

---

# Technical Reference

This section covers the internal design, security model, IPC choices, and packaging strategy in full detail. It is aimed at contributors and users who need to understand or extend LambdaFlow.

---

## Architecture

### Process model

LambdaFlow separates three concerns into three artifacts:

| Artifact | Role |
|---|---|
| `lambdaflow.windows.exe` | Host: window, WebView, IPC bridge, lifecycle |
| `frontend.pak` | Frontend: ZIP of HTML/CSS/JS served from a controlled origin |
| `backend/Backend.exe` | Backend: arbitrary executable compiled by the developer |

The host is the only entrypoint. It owns startup, shutdown, and all communication between the other two. The frontend and backend never communicate directly.

### Startup sequence

```
1. IntegrityVerifier.VerifyApplicationBundle()
     - Read lambdaflow.integrity.json
     - SHA-256 every file listed in the manifest
     - Abort on any mismatch or missing file

2. IPCBridge.Initialize()
     - Create a random Named Pipe (Windows ACL: current user only)
     - Start Backend.exe with LAMBDAFLOW_PIPE_NAME env var
     - Wait for backend to connect
     - Start async send/receive loops

3. WebView.Initialize(ipcBridge)
     - Create WinForms window from config.json
     - Initialize WebView2 environment
     - Inject window.send and window.receive stubs
     - Register WebResourceRequested handler for frontend.pak
     - Bind WebMessageReceived → IPCBridge.SendMessageToBackend

4. WebView.Navigate("index.html")
     - Resolved inside frontend.pak via virtual origin
     - CSP headers applied on every response

5. Application.Run()
     - Message loop, window handles events
     - On close: IPC bridge disposed, backend process terminated
```

### Message flow

```
Frontend (JS)                 Host (C#)               Backend (any language)
    │                             │                             │
    │── send(msg) ───────────────►│                             │
    │                             │── pipe.WriteLine(msg) ─────►│
    │                             │                             │── process(msg)
    │                             │◄── pipe.ReadLine(resp) ─────│
    │◄── window.receive(resp) ───│                             │
    │                             │                             │
```

All messages are strings and line-terminated in both directions. The optional Entity v1 ontology adds a shared typed-object convention across frontend and backend languages.

---

## Frontend Loading

The frontend is served through a virtual application origin:

```
https://app.lambdaflow.localhost/
```

WebView2 intercepts every request to this origin via `AddWebResourceRequestedFilter` and resolves the path inside `frontend.pak` (a ZIP archive). No file is ever read directly from disk at runtime; the PAK is sealed at build time.

The host DNS-maps `app.lambdaflow.localhost` to `0.0.0.0` via `--host-resolver-rules` to prevent any real network lookup for that hostname.

Every response includes:

```
Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline';
                         style-src 'self' 'unsafe-inline'; img-src 'self' data:;
                         font-src 'self' data:; connect-src 'none';
                         base-uri 'self'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
Content-Type: <extension-based MIME>
```

`connect-src 'none'` means the frontend cannot make arbitrary network requests. If an application needs network access, it must route through the backend, which is an intentional design constraint.

Path traversal is blocked at two points: in `GetPakRelativePath` (rejects `..` segments before looking up the PAK entry) and in `IntegrityVerifier` (rejects manifest paths that are absolute or contain `..`).

---

## IPC Design

### Named Pipe (Windows default)

A new random pipe name (`lambdaflow-<GUID>`) is created for each run. The backend receives the name via environment variable and connects within a timeout. The pipe is created with `PipeOptions.CurrentUserOnly`, which restricts access to the current user's SID at the OS level — no other user on the machine can connect to the pipe, even if they discover the GUID name. The random name further reduces the attack surface by making enumeration impractical.

The host uses a `Channel<string>`-backed send queue so frontend messages are serialized before writing to the pipe. The receive loop runs on a separate task.

### stdio fallback

When `ipcTransport` is set to `StdIO` in `config.json`, the backend's standard streams are redirected and used as the message channel. This is the universal fallback: any language that supports reading from stdin and writing to stdout can use it. The tradeoff is that backend diagnostic output (logs, errors) must go to stderr when using this transport, not stdout, because stdout is the message channel.

### Transport selection

The transport is set in `config.json` under `ipcTransport`:

```json
"ipcTransport": "NamedPipe"   // recommended
"ipcTransport": "StdIO"       // fallback
```

The backend detects the active transport via the `LAMBDAFLOW_IPC_TRANSPORT` environment variable. If absent or not `NamedPipe`, it should fall back to stdin/stdout.

### Future transports

| Platform | Recommended default | Rationale |
|---|---|---|
| Windows | Named Pipe | Native, fast, ACL-controllable |
| Linux | Unix domain socket | POSIX standard, file-permission ACL |
| macOS | Unix domain socket | Same as Linux |
| Android | Embedded service model | Sandboxing makes child executables impractical |

Shared memory is not planned as a base transport. It requires its own framing, synchronization, and crash handling — the line-oriented model covers the common case cleanly. Shared memory could be added as an opt-in bulk-data channel behind `IIPCBridge` for high-throughput payloads.

---

## Integrity Verification

At startup, `IntegrityVerifier.VerifyApplicationBundle()` reads `lambdaflow.integrity.json` and computes SHA-256 for every listed file. The manifest itself is excluded from its own hash list. If any file is missing, added, or modified, the app refuses to start.

The manifest is generated by `IntegrityManifestWriter.Write(appDir)` as the final step of `lambdaflow build`. It is deterministic and sorted by path.

**Current limitation**: the manifest file itself is not signed. An attacker who can write to the application directory can replace both a file and its hash. The runtime check defends against accidental corruption and simple file swaps, but not against a targeted attacker with write access to the bundle.

**Planned mitigation**: at build time, sign the manifest with an asymmetric key; embed the corresponding public key in the host binary as a compile-time constant. At startup, verify the manifest signature before reading any hash. This makes tampering detectable even when the attacker can modify the manifest, because the host public key cannot be changed without recompiling and re-signing the host. This should be combined with Authenticode signing of the host executable for full production hardening.

---

## Security Model

LambdaFlow is not a sandbox. The frontend, backend, and host are parts of one trusted application. These are the current protections and their scope:

| Protection | What it covers | What it does not cover |
|---|---|---|
| Integrity manifest | Accidental corruption, simple file swaps | Targeted attacker with write access |
| Named Pipe ACL | Other processes on the machine connecting to the pipe | The backend process itself (it is trusted) |
| CSP + nosniff | Inline script injection, MIME sniffing | Logic bugs in frontend code |
| `connect-src 'none'` | Frontend making arbitrary outbound network calls | Backend making network calls |
| PAK virtual origin | Direct file:// access, path traversal | Content inside the PAK (trusted at build time) |
| No shell execution | Shell injection via backend path | N/A — path is resolved from config, not user input |
| DevTools behind DebugMode | Accidental exposure in production builds | Deliberate debug builds |

**What must be added before production distribution:**

1. Sign the integrity manifest with a private key; verify with a public key embedded in the host.
2. Sign the Windows binaries with Authenticode (code signing certificate).
3. Use an installer that places the app in a write-protected directory (e.g. `%ProgramFiles%`).
4. Define and validate the message schema at the backend boundary — never trust the frontend message as safe input.
5. Keep secrets out of `frontend.pak`; it is readable by anyone who can access the install directory.

---

## Build System

`lambdaflow build` performs these steps in order:

1. Read `config.json` from the project directory.
2. Run `platforms.windows.archs.x64.compileCommand` in the backend source folder.
3. Copy the backend output (`compileDirectory`) to `Results/<name>-<version>/windows-x64/backend/`.
4. `dotnet publish` the LambdaFlow Windows host (self-contained, `win-x64`) into the same directory.
5. Copy `config.json` into the output directory.
6. Create `frontend.pak` as a ZIP of the frontend folder.
7. Run `IntegrityManifestWriter.Write()` — hash every output file, write `lambdaflow.integrity.json`.

The result is a self-contained directory ready to run or package.

The `compileCommand` is a shell string and can be any command the developer's toolchain supports:

```json
"compileCommand": "dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin"
"compileCommand": "mvn package -DskipTests"
"compileCommand": "cargo build --release"
"compileCommand": "pyinstaller main.py --onefile --distpath bin"
```

---

## Project Scaffolding Internals

`lambdaflow new` now has an explicit language-template pipeline.

Command shape:

```powershell
lambdaflow new <AppName> [directory] \
  [--framework <LambdaFlowRepo>] \
  [--language <csharp|java|python>] \
  [--backend-compile-command "..."] \
  [--backend-compile-directory "..."] \
  [--self-contained]
```

What happens internally:

1. Resolve framework root (`--framework`, `LAMBDAFLOW_HOME`, or parent-folder discovery).
2. Parse language template and choose defaults for compile + runtime backend settings.
3. Copy `Examples/<Language>/backend` and `Examples/<Language>/frontend` into the new project.
4. Exclude build artifacts while copying (`bin`, `obj`, `target`, `Results`, `__pycache__`).
5. Copy the selected language SDK helper into `lambdaflow/Sdk/<Language>/` and patch backend references to use it.
6. Generate `config.json` with:
   - backend compile settings (`compileCommand`, `compileDirectory`)
   - backend runtime launch settings (`runCommand`, `runArgs`)
   - window/app defaults
7. Generate `.vscode/tasks.json` and `.vscode/launch.json`.
8. If `--self-contained` is present, copy framework source into the new project under `lambdaflow/`.

The VS Code extension uses this exact command path. Its New Project wizard simply collects user input (name, language, compile command, compile directory) and invokes `lambdaflow new` with those flags.

---

## Cross-Platform Portability

The core design is portable. The platform-specific parts are isolated behind interfaces:

```
IWebView     — window creation, frontend loading, JS bridge
IIPCBridge   — transport setup, send/receive loops
IServices    — factory for the above, selected by platform at startup
```

Adding a new platform means implementing these three interfaces. The core logic (`Config`, `IntegrityVerifier`, `BackendProcess`, the CLI) does not change.

Practical notes for future platforms:

- **Linux**: WebKitGTK for the view, Unix domain socket or named pipe for IPC. GTK requires a different UI thread model than WinForms.
- **macOS**: WKWebView, `WKScriptMessageHandler` for the JS bridge, Unix domain socket for IPC. Must run on the main thread with `NSRunLoop`.
- **Android**: Mobile sandboxing makes arbitrary child executables impractical. The backend story is different — likely an embedded runtime, a bound Android Service, or a restricted set of languages compiled to Android ABIs. The frontend layer (WebView) is available, but the IPC and backend models require a separate design.

---

## Config Reference

`config.json` fields recognized by the host at runtime:

| Field | Type | Default | Description |
|---|---|---|---|
| `appName` | string | `"LambdaFlowApp"` | Application name |
| `appVersion` | string | `"1.0.0"` | Version string |
| `organizationName` | string | `"LambdaFlow"` | Organization name |
| `appIcon` | string | `"app.ico"` | Icon file (relative to app dir) |
| `frontendInitialHTML` | string | `"index.html"` | Entry point inside `frontend.pak` |
| `securityMode` | string | `"Hardened"` | Only `"Hardened"` is supported |
| `ipcTransport` | string | `"NamedPipe"` | `"NamedPipe"` or `"StdIO"` |
| `window.title` | string | `"LambdaFlow app"` | Window title bar |
| `window.width` | int | `800` | Initial width in pixels |
| `window.height` | int | `600` | Initial height in pixels |
| `window.minWidth` | int | `800` | Minimum width |
| `window.minHeight` | int | `600` | Minimum height |
| `window.maxWidth` | int | `0` | Maximum width (0 = unlimited) |
| `window.maxHeight` | int | `0` | Maximum height (0 = unlimited) |
| `platforms.windows.archs.x64.runCommand` | string | `"Backend.exe"` | Executable or command used to launch the backend inside `backend/` |
| `platforms.windows.archs.x64.runArgs` | string[] | `[]` | Arguments passed to `runCommand` |

`config.json` fields used by the CLI at build time:

| Field | Type | Default | Used by |
|---|---|---|---|
| `developmentBackendFolder` | string | `"backend"` | CLI (`lambdaflow build`) — working directory for backend compile command |
| `developmentFrontendFolder` | string | `"frontend"` | CLI — source directory packed into `frontend.pak` |
| `resultFolder` | string | `"Results"` | CLI — destination root for built artifacts |
| `platforms.windows.archs.x64.compileCommand` | string | language-dependent | CLI — command executed to compile backend |
| `platforms.windows.archs.x64.compileDirectory` | string | language-dependent | CLI — folder copied into final `backend/` output |

Summary:

- Host runtime uses app/window/security/ipc fields plus `runCommand` and `runArgs`.
- CLI build uses folder/build fields plus compile settings.

---

## Installer Direction

The CLI builds an app directory. Packaging that directory into an installer is the next step. Practical options for Windows:

| Option | Best for |
|---|---|
| MSIX | Modern signed packages, Microsoft Store distribution, enterprise MDM |
| WiX Toolset | Traditional MSI installers, enterprise environments |
| Self-extracting archive | Early development, simple distribution |

The recommended path for production distribution is MSIX or WiX combined with Authenticode signing. Writing to `%ProgramFiles%` via an installer ensures normal users cannot modify application files after installation, which strengthens the integrity manifest guarantee.
