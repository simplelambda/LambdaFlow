# Change Log

All notable changes to the LambdaFlow VS Code extension are documented here.

## 1.3.1 — 2026-07-23

- Fixed newly generated C# applications failing to connect when the packaged
  backend could not find a global .NET runtime.
- Added immediate startup failure reporting with backend exit codes and recent
  stderr instead of leaving an unresponsive frontend window open.
- Added Node.js and Go backend starters.
- Added Vue and Svelte frontend templates and startup connectivity checks for all
  Vite templates.
- Added safe framework cloning to the recommended location or a user-selected
  parent, plus selection of an existing checkout.
- Distinguished complete framework repositories from embedded self-contained
  project sources, so projects outside the LambdaFlow repository can be created,
  built, and run intuitively.
- Passed discovered .NET and Node.js toolchain paths to launched applications.
- Improved configuration round-tripping, Marketplace metadata, workspace trust
  declarations, documentation, and VSIX contents.

## 1.3.0 — 2026-07-23

- Added Windows and Linux project creation, build, run, and debug workflows.
- Added basic HTML and React frontend templates.
- Added C#, Java, Python, and custom backend selection.
- Added current-platform output detection for x64 and arm64.
- Added a visual `config.json` editor.
- Added framework, .NET, and Node.js path discovery on Windows and Linux.
- Added the LambdaFlow activity-bar view and project status.
