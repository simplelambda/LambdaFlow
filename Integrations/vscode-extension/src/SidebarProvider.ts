import * as vscode from 'vscode';
import * as fs     from 'fs';
import * as path   from 'path';
import * as crypto from 'crypto';
import { resolveFrameworkPath } from './utils';

interface ProjectInfo {
    appName:      string;
    appVersion:   string;
    ipcTransport: string;
}

export class SidebarProvider implements vscode.WebviewViewProvider {
    static readonly viewId = 'lambdaflow.sidebar';

    private _view?: vscode.WebviewView;

    constructor(private readonly _context: vscode.ExtensionContext) {}

    // -----------------------------------------------------------------------

    resolveWebviewView(webviewView: vscode.WebviewView): void {
        this._view = webviewView;
        webviewView.webview.options = { enableScripts: true };

        webviewView.webview.onDidReceiveMessage(
            (msg: { type: string }) => this._handleMessage(msg),
            null,
            this._context.subscriptions
        );

        vscode.workspace.onDidChangeWorkspaceFolders(
            () => this._refresh(), null, this._context.subscriptions
        );

        vscode.workspace.onDidSaveTextDocument(doc => {
            if (doc.fileName.endsWith('config.json')) this._refresh();
        }, null, this._context.subscriptions);

        this._refresh();
    }

    refresh(): void { this._refresh(); }

    // -----------------------------------------------------------------------

    private _refresh(): void {
        if (!this._view) return;
        this._view.webview.html = this._html(this._detectProject());
    }

    private _detectProject(): ProjectInfo | null {
        const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        if (!root) return null;

        const configPath = path.join(root, 'config.json');
        if (!fs.existsSync(configPath)) return null;

        try {
            const cfg = JSON.parse(fs.readFileSync(configPath, 'utf8'));
            return {
                appName:      String(cfg.appName      ?? 'Unknown'),
                appVersion:   String(cfg.appVersion   ?? '?'),
                ipcTransport: String(cfg.ipcTransport ?? 'NamedPipe')
            };
        } catch {
            return null;
        }
    }

    private _handleMessage(msg: { type: string }): void {
        switch (msg.type) {
            case 'newProject':  vscode.commands.executeCommand('lambdaflow.newProject');   break;
            case 'editConfig':  vscode.commands.executeCommand('lambdaflow.openConfig');   break;
            case 'build':       vscode.commands.executeCommand('lambdaflow.buildProject'); break;
            case 'run':         vscode.commands.executeCommand('lambdaflow.runProject');   break;
        }
    }

    // -----------------------------------------------------------------------

    private _html(project: ProjectInfo | null): string {
        const nonce   = crypto.randomBytes(16).toString('hex');
        const csp     = `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';`;
        const isReady = resolveFrameworkPath() !== null;

        const projectSection = project
            ? /* html */`
                <div class="section">
                    <div class="section-label">Project open</div>
                    <div class="card">
                        <div class="card-name">${esc(project.appName)}</div>
                        <div class="card-meta">v${esc(project.appVersion)} &middot; ${esc(project.ipcTransport)}</div>
                    </div>
                    <button class="secondary" id="btnEditConfig">Edit Configuration</button>
                    <button class="secondary" id="btnBuild">Build</button>
                    <button id="btnRun">&#9654; Run</button>
                </div>`
            : /* html */`
                <div class="empty">
                    Open a LambdaFlow project folder to manage its configuration and build it.
                </div>`;

        const warningBanner = !isReady
            ? /* html */`
                <div class="warning">
                    Framework not found.<br>
                    Set <code>lambdaflow.frameworkPath</code> in Settings, or open a self-contained project.
                </div>`
            : '';

        return /* html */`<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<style>
  *, *::before, *::after { box-sizing: border-box; }

  body {
    margin: 0;
    padding: 12px 12px 20px;
    font-family: var(--vscode-font-family);
    font-size:   var(--vscode-font-size);
    color:       var(--vscode-foreground);
  }

  .header {
    display: flex;
    align-items: center;
    gap: 8px;
    padding-bottom: 10px;
    margin-bottom: 14px;
    border-bottom: 1px solid var(--vscode-panel-border);
  }

  .logo {
    font-size: 1.5em;
    font-weight: 800;
    line-height: 1;
    color: var(--vscode-textLink-foreground);
    font-family: Georgia, serif;
    font-style: italic;
    user-select: none;
  }

  .brand {
    font-weight: 600;
    font-size: 0.95em;
    letter-spacing: 0.01em;
  }

  .section { margin-top: 14px; }

  .section-label {
    font-size: 0.72em;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.07em;
    color: var(--vscode-descriptionForeground);
    margin-bottom: 7px;
  }

  button {
    display: block;
    width: 100%;
    padding: 6px 10px;
    margin-bottom: 6px;
    border: none;
    cursor: pointer;
    font: inherit;
    font-size: 0.88em;
    border-radius: 3px;
    text-align: left;
    background: var(--vscode-button-background);
    color:      var(--vscode-button-foreground);
    transition: background 0.1s;
  }

  button:hover { background: var(--vscode-button-hoverBackground); }

  button.secondary {
    background: var(--vscode-button-secondaryBackground, #3c3c3c);
    color:      var(--vscode-button-secondaryForeground, #ccc);
  }

  button.secondary:hover {
    background: var(--vscode-button-secondaryHoverBackground, #494949);
  }

  .card {
    background: var(--vscode-input-background);
    border: 1px solid var(--vscode-panel-border);
    border-radius: 4px;
    padding: 8px 10px;
    margin-bottom: 8px;
  }

  .card-name {
    font-weight: 600;
    font-size: 0.92em;
  }

  .card-meta {
    font-size: 0.78em;
    color: var(--vscode-descriptionForeground);
    margin-top: 2px;
  }

  .empty {
    font-size: 0.83em;
    color: var(--vscode-descriptionForeground);
    line-height: 1.55;
    padding: 6px 0 0;
  }

  .warning {
    font-size: 0.8em;
    line-height: 1.5;
    color: var(--vscode-editorWarning-foreground, #cca700);
    background: var(--vscode-inputValidation-warningBackground, rgba(204,167,0,.12));
    border: 1px solid var(--vscode-inputValidation-warningBorder, #cca700);
    border-radius: 3px;
    padding: 7px 9px;
    margin-bottom: 12px;
  }

  code {
    font-family: var(--vscode-editor-font-family, monospace);
    font-size: 0.9em;
    background: var(--vscode-textCodeBlock-background, rgba(255,255,255,.1));
    padding: 1px 3px;
    border-radius: 2px;
  }
</style>
</head>
<body>

<div class="header">
  <span class="logo">&#955;</span>
  <span class="brand">LambdaFlow</span>
</div>

${warningBanner}

<div class="section">
  <div class="section-label">New</div>
  <button id="btnNewProject">+ New Project</button>
</div>

${projectSection}

<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  function post(type) { vscode.postMessage({ type }); }

  document.getElementById('btnNewProject').addEventListener('click', () => post('newProject'));
  document.getElementById('btnEditConfig')?.addEventListener('click', () => post('editConfig'));
  document.getElementById('btnBuild')?.addEventListener('click', () => post('build'));
  document.getElementById('btnRun')?.addEventListener('click', () => post('run'));
</script>
</body>
</html>`;
    }
}

// ---------------------------------------------------------------------------

function esc(value: string): string {
    return value
        .replace(/&/g,  '&amp;')
        .replace(/"/g,  '&quot;')
        .replace(/</g,  '&lt;')
        .replace(/>/g,  '&gt;');
}
