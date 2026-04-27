# LambdaFlow — VS Code Extension

Build, configure, and manage **LambdaFlow** desktop applications without ever leaving VS Code.

LambdaFlow lets you ship desktop apps with a web frontend and any backend language — your web UI runs inside a native WebView2 window, connected to your process via a secure named-pipe IPC channel. This extension wires the framework's tooling directly into the editor.

---

## Requirements

| Requirement | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | Needed to run the LambdaFlow CLI |
| LambdaFlow source tree | Clone from the repository and set the path in Settings |
| Windows 10 / 11 | WebView2 is Windows-only for now |

---

## Getting started

1. **Clone the LambdaFlow framework** to a local directory (e.g. `C:\Dev\LambdaFlow`).
2. **Open VS Code Settings** (`Ctrl+,`) and search for `LambdaFlow`.
3. Set **`LambdaFlow: Framework Path`** to the root of the cloned repository.
4. Click the **λ icon** in the Activity Bar on the left to open the LambdaFlow sidebar.

---

## Sidebar

The LambdaFlow sidebar has two states:

### No project open

A prompt and a **+ New Project** button are shown. Click it to launch the new-project wizard.

### Project open

When the active workspace folder contains a `config.json` at its root, the sidebar shows:

- The project name, version, and IPC transport
- **Edit Configuration** — opens the visual config editor
- **Build** — compiles the backend and packages the frontend into a distributable bundle

---

## New Project wizard

**Command:** `LambdaFlow: New Project`  
**Sidebar button:** `+ New Project`

Walks you through five prompts:

1. **Application name** — used as the project name and default window title
2. **Backend template language** — `C#`, `Java`, or `Python`
3. **Target directory** — where the project will be created (defaults to `<workspace>/Apps/<name>`)
4. **Backend compile command** — prefilled from language defaults, fully editable
5. **Backend compile output directory** — prefilled from language defaults, fully editable

The CLI runs in an integrated terminal. When it finishes, you'll be offered an **Open Folder** button to jump straight into the new project.

Language defaults used by the wizard:

| Language | Compile command default | Compile directory default |
|---|---|---|
| C# | `dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin` | `bin` |
| Java | `mvn -q -DskipTests package` | `target` |
| Python | `python build.py` | `bin` |

> The framework path must be configured before running this command.

---

## Configuration editor

**Command:** `LambdaFlow: Edit Configuration`  
**Sidebar button:** `Edit Configuration`

Opens a visual editor for `config.json` with sections for:

| Section | Fields |
|---|---|
| **App** | Name, version, organization, app icon path |
| **Window** | Title, initial size, min/max size |
| **Frontend** | Entry HTML, source folder |
| **Backend** | Source folder, Windows x64 compile command and output directory |
| **Security & IPC** | IPC transport (Named Pipe or StdIO); security mode is always *Hardened* |
| **Output** | Result folder for build artifacts |

Click **Save** to write changes back to `config.json`. Click **Reset** to revert to the values on disk.

---

## Build

**Command:** `LambdaFlow: Build`  
**Sidebar button:** `Build`

Runs the LambdaFlow CLI build in an integrated terminal. The CLI:

1. Compiles the backend using the command in `config.json`
2. Packs the frontend folder into `frontend.pak`
3. Generates `lambdaflow.integrity.json` (SHA-256 manifest verified at every launch)
4. Copies the host executable and result into the configured result folder

---

## Extension settings

| Setting | Default | Description |
|---|---|---|
| `lambdaflow.frameworkPath` | *(empty)* | Absolute path to the LambdaFlow source directory. Required for all commands. |

Set this in **User** or **Machine** scope — not Workspace, so it works across all your projects.

---

## Commands

All commands are accessible from the Command Palette (`Ctrl+Shift+P`):

| Command | Description |
|---|---|
| `LambdaFlow: New Project` | Create a new app from a template |
| `LambdaFlow: Build` | Build the open project |
| `LambdaFlow: Edit Configuration` | Open the visual config editor |

---

## How it works

```
Activity Bar (λ)
    └── Sidebar panel (WebviewView)
            ├── Reads config.json from the workspace root
            ├── Detects project: name / version / IPC transport
            └── Sends messages → VS Code commands
                    ├── lambdaflow.newProject  → InputBox wizard → Terminal
                    ├── lambdaflow.openConfig  → WebviewPanel (ConfigEditorPanel)
                    └── lambdaflow.buildProject → Terminal
```

The sidebar refreshes automatically when `config.json` is saved or the workspace folder changes.
