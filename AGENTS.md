# LambdaFlow agent guide

This file is the minimum working context for code agents and developers. It applies to the entire repository. Do not scan the whole repository before working: start with the task-to-files table below, then open only the referenced files.

## Product contract

LambdaFlow creates native desktop applications whose UI is HTML/CSS/JavaScript and whose backend is any executable.

The immutable architectural contract is:

```text
frontend.pak → native webview host ⇄ JSON-lines IPC ⇄ backend process
```

- Windows host: WinForms + WebView2.
- Linux host: Photino.NET + GTK 3 + WebKitGTK 4.1.
- Windows transport under `Auto`: private named pipe.
- Linux transport under `Auto`: redirected stdin/stdout.
- The same `config.json`, envelope format, frontend SDK, integrity rules, and backend concepts apply to both hosts.
- macOS is modeled but not implemented.

Do not introduce a required HTTP server, require Node.js in the backend, or couple application frontend code to WebView2/Photino.

## Read only what the task needs

| Task | Start with |
|---|---|
| Shared runtime/config/backend launch | `lambdaflow/Core/Config.cs`, `BackendProcess.cs`, relevant interface |
| Windows host/webview/IPC | `lambdaflow/Hosts/Windows/` |
| Linux host/webview/IPC | `lambdaflow/Hosts/Linux/` |
| Project creation | `lambdaflow/Tools/LambdaFlow.Cli/NewCommand.cs` |
| Packaging/target selection | `BuildCommand.cs`, `BuildTarget.cs`, `LambdaFlowConfig.cs` |
| Frontend API | `lambdaflow/Sdk/JavaScript/lambdaflow.js`, then `.d.ts` or `lambdaflowApi.ts` |
| Backend SDK | only the selected file under `lambdaflow/Sdk/<Language>/` |
| VS Code extension commands | `Integrations/vscode-extension/src/extension.ts`, `utils.ts` |
| Visual config editor | `Integrations/vscode-extension/src/ConfigEditorPanel.ts` |
| Sidebar | `Integrations/vscode-extension/src/SidebarProvider.ts` |
| Example behavior | one selected `Examples/<Language>/` tree |
| Protocol/entity schema | this file first; ontology details in `lambdaflow/Ontology/` |
| User documentation | `README.md` and matching `README.es.md` section |
| Release/security policy | project files, extension `package.json`, SDK version constants, `SECURITY.md`, `.gitignore` |

Generated/derived folders are not source:

- `**/bin/`
- `**/obj/`
- `**/target/`
- `**/Results/`
- `**/node_modules/`
- `Integrations/vscode-extension/out/` is compiled from `src/`; update it by running the TypeScript build.

Release `1.3.1` is declared in the four framework `.csproj` files, the extension
`package.json`, and each canonical SDK version constant. Keep those values
aligned for framework releases; example application `appVersion` values remain
independent.

## Runtime sequence

1. The packaged host reads `config.json` from `AppContext.BaseDirectory`.
2. `IntegrityVerifier` validates `lambdaflow.integrity.json`.
3. The platform host creates its services.
4. IPC starts the backend from packaged `backend/`.
5. The webview opens `frontend.pak` at a private local origin.
6. The host exposes `window.send(string)` and `window.receive(string)`.
7. `lambdaflow.js` replaces `window.receive` with the SDK dispatcher.
8. Messages are forwarded without the host interpreting application kinds.
9. Closing the native window disposes IPC and the backend process.

Keep this order. In particular, do not navigate before the bridge and packaged-resource handler exist.

## Public wire protocol

One UTF-8 JSON object per line:

```ts
type Envelope = {
  kind: string;             // required, non-empty
  id?: string;              // correlation id when a reply is required
  ok?: boolean;             // response status
  payload?: unknown;
  error?: {
    code?: string;
    message: string;
    details?: unknown;
  };
};
```

Request:

```json
{"kind":"text.uppercase","id":"uuid","payload":{"text":"hello"}}
```

Success:

```json
{"kind":"text.uppercase.result","id":"uuid","ok":true,"payload":{"text":"HELLO"}}
```

Failure:

```json
{"kind":"text.uppercase.result","id":"uuid","ok":false,"error":{"code":"INVALID_INPUT","message":"text is required"}}
```

Protocol invariants:

- Reuse the request `id` exactly.
- SDK-created response kinds append `.result`.
- Route pending responses by `id`, not by response `kind`.
- New code writes top-level `error`; retain read compatibility with legacy `payload.error`.
- Never write backend logs to stdout when StdIO is the transport; use stderr.
- Serialize each envelope on one line and flush after writing.
- Protect concurrent writers so JSON lines cannot interleave.
- Fire-and-forget messages normally have no `id` and must not cause automatic echo replies.
- `__lambdaflow_ready` and `__console` are host-reserved kinds. Do not forward them to the application backend.

## Entity payloads

Entity wrapper:

```json
{"$type":"animals.dog","$v":1,"data":{"name":"Rex"}}
```

- `$type`: required non-empty logical type.
- `$v`: integer schema version, minimum `1`.
- `data`: arbitrary JSON.
- SDK handlers unwrap `data` by default.
- Handler metadata must preserve raw payload, type, version, id, kind, and receive time.
- Use entities only where type identity/versioning is useful; ordinary payloads stay ordinary JSON.

## Frontend public API

Source of truth: `lambdaflow/Sdk/JavaScript/lambdaflow.js`.

### Availability/configuration

- `LambdaFlow.configure(options)`
  - `timeoutMs`
  - `unwrapEntities`
  - `warnOnUnhandled`
  - `logger`
  - `transportSend` for tests/adapters
- `isHostAvailable()` / `isAvailable()`
- `ensureHostAvailable()` / `ensureAvailable()`

### Send/request

- `send(kind, payload?, options?)`
- `emit(...)`: alias of `send`
- `sendEnvelope(envelope)`: low-level
- `request(kind, payload?, timeoutOrOptions?)`
  - options: `timeoutMs`, `unwrap`, `signal`, `id`
- `requestEntity(kind, type, data, timeoutOrOptions?, version?)`

### Receive/handlers

- `on(kind, handler, options?)`
- `receive(...)`: alias of `on`
- `onAny(handler, options?)`
- `once(kind, handler, options?)`
- `off(kind, handler?)`
- `handle(kind, handler, options?)`: handles backend-to-frontend requests
- `unhandle(kind)`
- `respond(kind, id, payload?)`
- `reject(kind, id, error)`

Event handlers receive `(payload, meta)`. Request handlers may return a value or Promise; the SDK builds success/error responses.

### Entities/lifecycle

- `entity(type, data, version?)`
- `sendEntity(kind, type, data, version?, options?)`
- `isEntity(payload)`
- `unwrapEntity(payload)`
- `entityType(payload)`
- `entityVersion(payload)`
- `receiveRaw(raw)`: testing/custom adapter entrypoint
- `pendingCount()`
- `clearHandlers()`
- `destroy()`

When changing this API:

1. Preserve existing names and behavior where possible.
2. Update `lambdaflow.d.ts`.
3. Update `lambdaflowApi.ts`.
4. Update the frontend copies in all examples if `lambdaflow.js` changed.
5. Update both README languages and this API list.
6. Run syntax, type, and protocol tests.

## Backend SDK common model

Canonical files:

- C#: `lambdaflow/Sdk/CSharp/LambdaFlow.cs`
- Java: `lambdaflow/Sdk/Java/LambdaFlow.java`
- Python: `lambdaflow/Sdk/Python/lambdaflow.py`

Equivalent operations:

| Operation | C# | Java | Python |
|---|---|---|---|
| SDK version | `Version` | `VERSION` | `__version__`/`VERSION` |
| Configure | `Configure` | `configure` | `configure` |
| Register | `Receive`/`On`/`Handle` | `receive`/`on`/`handle` | `receive`/`on`/`handle` |
| Remove | `Unhandle`/`Off` | `unhandle`/`off` | `unhandle`/`off` |
| Event | `Send`/`Emit` | `send`/`emit` | `send`/`emit` |
| Request frontend | `Request`/`RequestAsync` | `request`/`requestAsync` | `request` |
| Manual response | `Respond`/`Reject` | `respond`/`reject` | `respond`/`reject` |
| Entity | `Entity` | `entity` | `entity` |
| Run | `Run`/`RunAsync` | `run` | `run` |
| Stop | `Stop` | `stop` | `stop` |
| Pending count | `PendingCount` | `pendingCount` | `pending_count` |

Language conventions may change capitalization and async representation, but semantics and envelope shapes must match.

SDK practices:

- Register handlers before starting the run loop.
- A handler return value becomes a correlated response only when incoming `id` exists.
- Exceptions become `ok: false` with a structured error.
- The input loop must remain able to receive a response while a handler waits on a backend-to-frontend request.
- Transport selection is automatic from `LAMBDAFLOW_IPC_TRANSPORT` and `LAMBDAFLOW_PIPE_NAME`.
- StdIO is the fallback when transport variables are absent.
- Keep SDKs single-file and easy for the CLI to copy.

## Configuration contract

Top-level runtime/build fields:

```text
appName
appVersion
organizationName
appIcon
securityMode                 only Hardened
ipcTransport                 Auto | NamedPipe | StdIO
developmentBackendFolder
developmentFrontendFolder
resultFolder
frontendInitialHTML
build.preBuild[]
debug
platforms.<os>.archs.<arch>
window
```

Architecture entry:

```json
{
  "compileCommand": "...",
  "compileDirectory": "bin/linux-x64",
  "runCommand": "Backend",
  "runArgs": []
}
```

Keep native backend outputs in target-specific directories (`bin/win-x64`,
`bin/linux-x64`, and so on). The CLI copies the whole configured directory.

Supported build target keys:

```text
platforms.windows.archs.x64     → windows-x64 / win-x64
platforms.windows.archs.arm64   → windows-arm64 / win-arm64
platforms.linux.archs.x64       → linux-x64
platforms.linux.archs.arm64     → linux-arm64
```

`Auto` resolves to NamedPipe on Windows and StdIO on Linux. For portable compatibility, explicit legacy `NamedPipe` also degrades to StdIO on Linux.

Paths used by build must stay inside the project. Do not weaken the containment checks before recursive output cleanup.

## CLI behavior

Entrypoint: `lambdaflow/Tools/LambdaFlow.Cli/Program.cs`.

Create:

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  new <AppName> [directory] \
  --framework <repo> \
  --language <csharp|java|python|node|go|other> \
  --frontend <basic|react|vue|svelte> \
  [--debug] [--self-contained]
```

Build:

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build [projectDirectory] \
  --framework <repo> \
  [--target <windows-x64|windows-arm64|linux-x64|linux-arm64>] \
  [--debug]
```

If target is omitted, use the current supported OS/architecture.

Build order:

1. Read config and resolve target.
2. Validate project-contained paths.
3. Run enabled prebuild commands in order.
4. Run target backend compile command.
5. Copy compile output to package `backend/`.
6. Publish the target host self-contained.
7. Rename host to app name.
8. Copy config and optional icon.
9. Zip frontend into `frontend.pak`.
10. Generate integrity manifest last.

Never include `Results`, `bin`, `obj`, `target`, caches, or `node_modules` when scaffolding source.

Template-specific rules:

- Generated C# backends publish with `--self-contained true`; do not reintroduce
  a target-machine .NET runtime dependency.
- Node.js and Go templates are dependency-free protocol starters rather than
  canonical full backend SDKs.
- React, Vue, and Svelte use Vite, load the canonical `lambdaflow.js` before the
  framework entrypoint, and issue `backend.ping` on startup.
- Generated projects include a `.gitignore` for packages, compiler output,
  dependencies, and caches.

## Host-specific constraints

### Windows

- Preserve the existing WebView2 implementation unless the task requires it.
- Named pipe must remain `CurrentUserOnly`.
- WebView2 work touching controls must marshal to the UI thread.
- Keep the synthetic HTTPS local origin and CSP headers.
- Debug features are gated by `debug.enabled`.

### Linux

- One implementation covers distributions; do not fork host code per distro.
- Runtime dependencies are GTK 3 and WebKitGTK 4.1.
- Serve frontend through the `lambdaflow://app/` custom scheme.
- Reject `.`/`..` path segments after URL decoding.
- Inject bridge bootstrap and CSP before page application scripts.
- Photino web messages use `window.external.sendMessage/receiveMessage`; application code still sees only `window.send/receive`.
- Use StdIO IPC.
- Use absolute icon paths.

## Security invariants

- Verify integrity before starting backend or webview.
- Generate the manifest only after every package file is final.
- Manifest paths are relative and cannot escape the app directory.
- SHA-256 integrity is tamper detection, not publisher authenticity.
- Do not enable filesystem access, host objects, arbitrary remote origins, or relaxed CSP by default.
- DevTools, context menus, console capture, and extra logs are debug-only.
- Never execute `runCommand` through a shell; use `ProcessStartInfo.ArgumentList`.

## VS Code extension

Source: `Integrations/vscode-extension/src/`.

- `extension.ts`: commands, current-target output resolution, download path.
- `ConfigEditorPanel.ts`: round-trips JSON while preserving unknown fields/architectures.
- `SidebarProvider.ts`: status/actions only.
- `utils.ts`: framework and CLI resolution.

The extension must:

- Work with Linux and Windows filesystem paths.
- Allow application projects to live outside the framework repository.
- Treat an embedded self-contained CLI as valid for build/run, but require the
  complete SDK/example template tree for project creation.
- Offer the recommended clone location, a user-selected clone parent, or an
  existing valid checkout when the framework is missing.
- Clone through a temporary sibling and never delete a pre-existing invalid
  destination.
- Use the CLI rather than duplicating build logic.
- Find results via configured `resultFolder`, app name/version, and current target.
- Add `.exe` only on Windows.
- Preserve config fields it does not edit.

After source changes:

```bash
cd Integrations/vscode-extension
npm run compile
```

`out/` is ignored generated output. The `vscode:prepublish` script compiles it
before VSIX packaging; commit the TypeScript sources, not `out/*.js`.

## Required documentation discipline

`README.md` is English. `README.es.md` is the equivalent Spanish document.

For user-visible behavior changes:

- Update the relevant section in both files.
- Keep both tables of contents searchable.
- Keep examples operational on both supported hosts.
- Update this file if architecture, public API, config, workflow, or invariants changed.
- Keep `SECURITY.md` and the final disclaimer section in both READMEs aligned.

## Focused validation

Use the smallest relevant set, then run the full build before handoff.

```bash
# Shared CLI and hosts
dotnet build lambdaflow/Tools/LambdaFlow.Cli/LambdaFlow.Cli.csproj -c Release
dotnet build lambdaflow/Hosts/Linux/lambdaflow.linux.csproj -c Release
dotnet build lambdaflow/Hosts/Windows/lambdaflow.windows.csproj -c Release

# C# SDK/template
dotnet build Examples/CSharp/backend/Backend.csproj -c Release

# Python SDK
python -m py_compile lambdaflow/Sdk/Python/lambdaflow.py

# Java SDK/template
mvn -q -f Examples/Java/backend/pom.xml -DskipTests package

# Frontend SDK
node --check lambdaflow/Sdk/JavaScript/lambdaflow.js

# Additional protocol starters
printf '%s\n' '{"kind":"backend.ping","id":"smoke-node"}' \
  | node Examples/Node/backend/backend.mjs
printf '%s\n' '{"kind":"backend.ping","id":"smoke-go"}' \
  | go run Examples/Go/backend/backend.go

# Extension
cd Integrations/vscode-extension
npm run compile
```

Package both principal targets when changing CLI/config/host contracts:

```bash
dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build <project> --framework . --target linux-x64

dotnet run --project lambdaflow/Tools/LambdaFlow.Cli -- \
  build <project> --framework . --target windows-x64
```

For SDK protocol smoke tests, feed the same request to each backend and compare logical output:

```json
{"kind":"uppercase","id":"smoke-1","payload":{"text":"hola"}}
```

Expected:

```json
{"kind":"uppercase.result","id":"smoke-1","ok":true,"payload":{"text":"HOLA"}}
```

Linux GUI validation requires a desktop session plus GTK/WebKitGTK. Windows GUI validation requires a Windows host/VM with WebView2; cross-compilation alone is not a GUI test.

## Change checklist

- Preserve public compatibility unless explicitly changing a versioned contract.
- Add Linux and Windows config/build behavior together when platform-neutral.
- Keep SDK envelope behavior aligned.
- Update generator output, examples, extension, docs, and this file when their contract changes.
- Do not edit generated build artifacts as source.
- Do not remove or overwrite unrelated user work in a dirty worktree.
- Report which tests were executed and which platform behavior still requires a real OS/VM.
