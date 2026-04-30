import * as vscode from 'vscode';
import * as crypto from 'crypto';

// ---------------------------------------------------------------------------
// Types

interface WindowConfig {
    title:     string;
    width:     number;
    height:    number;
    minWidth:  number;
    minHeight: number;
    maxWidth:  number;
    maxHeight: number;
}

interface ArchConfig {
    compileCommand:   string;
    compileDirectory: string;
}

interface DebugConfig {
    enabled:                  boolean;
    frontendDevTools:         boolean;
    openFrontendDevToolsOnStart: boolean;
    captureFrontendConsole:   boolean;
    showBackendConsole:       boolean;
    backendLogLevel:          string;
}

interface PreBuildCommandConfig {
    name?:            string;
    command:          string;
    workingDirectory: string;
    enabled?:         boolean;
    continueOnError?: boolean;
    timeoutSeconds?:  number | null;
}

interface BuildConfig {
    preBuild: PreBuildCommandConfig[];
}

interface LambdaFlowConfig {
    appName:                   string;
    appVersion:                string;
    organizationName:          string;
    appIcon:                   string;
    frontendInitialHTML:       string;
    securityMode:              string;
    ipcTransport:              string;
    resultFolder:              string;
    developmentBackendFolder:  string;
    developmentFrontendFolder: string;
    window:                    WindowConfig;
    build?:                    BuildConfig;
    debug?:                    DebugConfig;
    platforms: {
        windows?: { archs: { x64?: ArchConfig } };
        [key: string]: unknown;
    };
    [key: string]: unknown;
}

// ---------------------------------------------------------------------------
// Custom text editor provider

export class LambdaFlowConfigEditorProvider implements vscode.CustomTextEditorProvider {

    static readonly viewType = 'lambdaflow.configEditor';

    static register(_context: vscode.ExtensionContext): vscode.Disposable {
        return vscode.window.registerCustomEditorProvider(
            LambdaFlowConfigEditorProvider.viewType,
            new LambdaFlowConfigEditorProvider(),
            { webviewOptions: { retainContextWhenHidden: true } }
        );
    }

    // -----------------------------------------------------------------------

    resolveCustomTextEditor(
        document:     vscode.TextDocument,
        webviewPanel: vscode.WebviewPanel,
        _token:       vscode.CancellationToken
    ): void {
        webviewPanel.webview.options = { enableScripts: true };

        let ignoreNextChange = false;

        const update = () => {
            try {
                const config = JSON.parse(document.getText()) as LambdaFlowConfig;
                webviewPanel.webview.html = buildHtml(config);
            } catch {
                webviewPanel.webview.html = errorHtml();
            }
        };

        webviewPanel.webview.onDidReceiveMessage(async (msg: { type: string; config?: LambdaFlowConfig }) => {
            if (msg.type === 'save' && msg.config) {
                ignoreNextChange = true;
                try {
                    const text = JSON.stringify(msg.config, null, 2) + '\n';
                    const edit = new vscode.WorkspaceEdit();
                    edit.replace(document.uri, new vscode.Range(0, 0, document.lineCount, 0), text);
                    await vscode.workspace.applyEdit(edit);
                    await document.save();
                    vscode.window.showInformationMessage('LambdaFlow: Configuration saved.');
                } finally {
                    ignoreNextChange = false;
                }
            }

            if (msg.type === 'openAsJson') {
                vscode.window.showTextDocument(document.uri, { viewColumn: webviewPanel.viewColumn });
            }
        });

        const sub = vscode.workspace.onDidChangeTextDocument(e => {
            if (e.document.uri.toString() === document.uri.toString() && !ignoreNextChange) {
                update();
            }
        });
        webviewPanel.onDidDispose(() => sub.dispose());

        update();
    }
}

// ---------------------------------------------------------------------------
// HTML

function buildHtml(config: LambdaFlowConfig): string {
    const nonce = crypto.randomBytes(16).toString('hex');
    const csp   = `default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';`;
    const x64   = config.platforms?.windows?.archs?.x64 ?? { compileCommand: '', compileDirectory: 'bin' };
    const debug = normalizeDebug(config.debug);
    const preBuild = normalizePreBuild(config.build);

    return /* html */`<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="${csp}">
<title>LambdaFlow Configuration</title>
<style>
  *, *::before, *::after { box-sizing: border-box; }

  body {
    padding: 24px 28px 40px;
    font-family: var(--vscode-font-family);
    font-size:   var(--vscode-font-size);
    color:       var(--vscode-foreground);
    background:  var(--vscode-editor-background);
    max-width: 700px;
  }

  h1 {
    font-size: 1.1em;
    font-weight: 600;
    margin: 0 0 24px;
    padding-bottom: 10px;
    border-bottom: 1px solid var(--vscode-panel-border);
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .view-json {
    font-size: 0.78em;
    font-weight: 400;
    letter-spacing: 0;
    text-transform: none;
    color: var(--vscode-textLink-foreground);
    cursor: pointer;
    background: none;
    border: none;
    padding: 2px 4px;
    font: inherit;
    font-size: 0.78em;
    border-radius: 2px;
  }
  .view-json:hover { text-decoration: underline; }

  h2 {
    font-size: 0.78em;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--vscode-descriptionForeground);
    margin: 28px 0 10px;
  }

  .field { margin-bottom: 10px; }

  label {
    display: block;
    font-size: 0.9em;
    margin-bottom: 3px;
  }

  input[type="text"],
  input[type="number"],
  select {
    width: 100%;
    padding: 5px 8px;
    background: var(--vscode-input-background);
    color:      var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent);
    outline: none;
    font: inherit;
    border-radius: 2px;
  }

  input:focus, select:focus { border-color: var(--vscode-focusBorder); }

  .check {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 8px 0;
  }

  .check input { margin: 0; }

  .prebuild-list {
    display: grid;
    gap: 10px;
  }

  .prebuild-item {
    border: 1px solid var(--vscode-panel-border);
    border-radius: 4px;
    padding: 10px;
  }

  .prebuild-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 8px;
  }

  .prebuild-actions {
    display: flex;
    justify-content: space-between;
    gap: 8px;
    margin-top: 8px;
  }

  .prebuild-actions button {
    width: auto;
    padding: 5px 10px;
  }

  .grid-2 {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 10px;
  }

  .hint {
    font-size: 0.78em;
    color: var(--vscode-descriptionForeground);
    margin: 3px 0 0;
    line-height: 1.4;
  }

  .badge {
    display: inline-block;
    font-size: 0.72em;
    padding: 1px 6px;
    border-radius: 10px;
    background: var(--vscode-badge-background);
    color: var(--vscode-badge-foreground);
    vertical-align: middle;
    margin-left: 6px;
  }

  .actions {
    margin-top: 32px;
    display: flex;
    gap: 8px;
    padding-top: 16px;
    border-top: 1px solid var(--vscode-panel-border);
  }

  button {
    padding: 6px 18px;
    border: none;
    cursor: pointer;
    font: inherit;
    border-radius: 2px;
    background: var(--vscode-button-background);
    color:      var(--vscode-button-foreground);
  }

  button:hover { background: var(--vscode-button-hoverBackground); }

  button.secondary {
    background: var(--vscode-button-secondaryBackground, #3c3c3c);
    color:      var(--vscode-button-secondaryForeground, #cccccc);
  }

  button.secondary:hover {
    background: var(--vscode-button-secondaryHoverBackground, #494949);
  }
</style>
</head>
<body>

<h1>
  LambdaFlow Configuration
  <button class="view-json" id="btnViewJson">View JSON</button>
</h1>

<form id="form">

  <h2>App</h2>

  <div class="field">
    <label>Name</label>
    <input type="text" name="appName" value="${e(config.appName)}">
  </div>

  <div class="field">
    <label>Version</label>
    <input type="text" name="appVersion" value="${e(config.appVersion)}">
  </div>

  <div class="field">
    <label>Organization</label>
    <input type="text" name="organizationName" value="${e(config.organizationName)}">
  </div>

  <div class="field">
    <label>App Icon</label>
    <input type="text" name="appIcon" value="${e(config.appIcon)}">
    <p class="hint">Relative to the app directory (e.g. <code>app.ico</code>).</p>
  </div>

  <h2>Window</h2>

  <div class="field">
    <label>Title</label>
    <input type="text" name="windowTitle" value="${e(config.window.title)}">
  </div>

  <div class="grid-2">
    <div class="field">
      <label>Width</label>
      <input type="number" name="windowWidth" value="${config.window.width}" min="100">
    </div>
    <div class="field">
      <label>Height</label>
      <input type="number" name="windowHeight" value="${config.window.height}" min="100">
    </div>
  </div>

  <div class="grid-2">
    <div class="field">
      <label>Min Width</label>
      <input type="number" name="windowMinWidth" value="${config.window.minWidth}" min="0">
    </div>
    <div class="field">
      <label>Min Height</label>
      <input type="number" name="windowMinHeight" value="${config.window.minHeight}" min="0">
    </div>
  </div>

  <div class="grid-2">
    <div class="field">
      <label>Max Width</label>
      <input type="number" name="windowMaxWidth" value="${config.window.maxWidth}" min="0">
    </div>
    <div class="field">
      <label>Max Height</label>
      <input type="number" name="windowMaxHeight" value="${config.window.maxHeight}" min="0">
    </div>
  </div>
  <p class="hint">Set Max Width / Max Height to <strong>0</strong> for no limit.</p>

  <h2>Frontend</h2>

  <div class="field">
    <label>Entry HTML</label>
    <input type="text" name="frontendInitialHTML" value="${e(config.frontendInitialHTML)}">
    <p class="hint">Path inside <code>frontend.pak</code> that loads first.</p>
  </div>

  <div class="field">
    <label>Frontend Source Folder</label>
    <input type="text" name="developmentFrontendFolder" value="${e(config.developmentFrontendFolder)}">
    <p class="hint">Relative to project root. Packed into <code>frontend.pak</code> at build time.</p>
  </div>

  <h2>Pre-build Commands</h2>

  <div id="preBuildList" class="prebuild-list">
    ${renderPreBuildItems(preBuild)}
  </div>
  <p class="hint">Commands run in order before backend compilation and frontend packaging.</p>
  <div class="actions" style="margin-top:10px;padding-top:10px">
    <button type="button" class="secondary" id="btnAddPreBuild">Add Command</button>
  </div>

  <h2>Backend</h2>

  <div class="field">
    <label>Backend Source Folder</label>
    <input type="text" name="developmentBackendFolder" value="${e(config.developmentBackendFolder)}">
    <p class="hint">Relative to project root.</p>
  </div>

  <div class="field">
    <label>Windows x64 — Compile Command</label>
    <input type="text" name="winX64CompileCommand" value="${e(x64.compileCommand)}">
    <p class="hint">
      Runs inside the backend source folder. Output must land in the compile directory.<br>
      Examples: <code>dotnet publish Backend.csproj -c Release -r win-x64 -o bin</code>
      &nbsp;·&nbsp; <code>cargo build --release</code>
    </p>
  </div>

  <div class="field">
    <label>Windows x64 — Compile Directory</label>
    <input type="text" name="winX64CompileDirectory" value="${e(x64.compileDirectory)}">
    <p class="hint">Relative to backend source folder. Copied to <code>backend/</code> in the result.</p>
  </div>

  <h2>Security &amp; IPC</h2>

  <div class="field">
    <label>IPC Transport</label>
    <select name="ipcTransport">
      <option value="NamedPipe" ${config.ipcTransport === 'NamedPipe' ? 'selected' : ''}>Named Pipe — recommended on Windows</option>
      <option value="StdIO"     ${config.ipcTransport === 'StdIO'     ? 'selected' : ''}>StdIO — universal fallback</option>
    </select>
    <p class="hint">Named Pipe is faster and keeps protocol traffic separate from backend logs.</p>
  </div>

  <div class="field">
    <label>Security Mode <span class="badge">read-only</span></label>
    <select disabled>
      <option selected>Hardened</option>
    </select>
    <p class="hint">Only Hardened mode is supported. SHA-256 integrity check runs before every launch.</p>
  </div>

  <h2>Debug</h2>

  <label class="check">
    <input type="checkbox" name="debugEnabled" ${debug.enabled ? 'checked' : ''}>
    Enable debug mode
  </label>

  <label class="check">
    <input type="checkbox" name="debugFrontendDevTools" ${debug.frontendDevTools ? 'checked' : ''}>
    Enable WebView DevTools
  </label>

  <label class="check">
    <input type="checkbox" name="debugOpenFrontendDevToolsOnStart" ${debug.openFrontendDevToolsOnStart ? 'checked' : ''}>
    Open DevTools on start
  </label>

  <label class="check">
    <input type="checkbox" name="debugCaptureFrontendConsole" ${debug.captureFrontendConsole ? 'checked' : ''}>
    Capture frontend console
  </label>

  <label class="check">
    <input type="checkbox" name="debugShowBackendConsole" ${debug.showBackendConsole ? 'checked' : ''}>
    Show backend console/logs
  </label>

  <div class="field">
    <label>Backend Log Level</label>
    <input type="text" name="debugBackendLogLevel" value="${e(debug.backendLogLevel)}">
  </div>

  <h2>Output</h2>

  <div class="field">
    <label>Result Folder</label>
    <input type="text" name="resultFolder" value="${e(config.resultFolder)}">
    <p class="hint">Relative to project root. Build artifacts are written here.</p>
  </div>

  <div class="actions">
    <button type="submit" id="btnSave">Save</button>
    <button type="button" class="secondary" id="btnReset">Reset</button>
  </div>

</form>

<script nonce="${nonce}">
  const vscode   = acquireVsCodeApi();
  const original = ${JSON.stringify(config)};

  document.getElementById('btnViewJson').addEventListener('click', () => {
    vscode.postMessage({ type: 'openAsJson' });
  });

  document.getElementById('btnReset').addEventListener('click', () => populate(original));
  document.getElementById('btnAddPreBuild').addEventListener('click', () => appendPreBuildCommand({ enabled: true, command: '', workingDirectory: '' }));
  document.getElementById('preBuildList').addEventListener('click', ev => {
    if (ev.target?.dataset?.action === 'remove-prebuild') {
      ev.target.closest('.prebuild-item')?.remove();
    }
  });

  document.getElementById('form').addEventListener('submit', ev => {
    ev.preventDefault();
    const f   = document.getElementById('form');
    const get = name => f.elements[name]?.value ?? '';
    const num = name => parseInt(get(name), 10) || 0;
    const checked = name => !!f.elements[name]?.checked;

    const updated = {
      ...original,
      appName:                   get('appName'),
      appVersion:                get('appVersion'),
      organizationName:          get('organizationName'),
      appIcon:                   get('appIcon'),
      frontendInitialHTML:       get('frontendInitialHTML'),
      developmentFrontendFolder: get('developmentFrontendFolder'),
      developmentBackendFolder:  get('developmentBackendFolder'),
      ipcTransport:              get('ipcTransport'),
      securityMode:              'Hardened',
      resultFolder:              get('resultFolder'),
      build: {
        ...(original.build ?? {}),
        preBuild: collectPreBuild()
      },
      debug: {
        ...(original.debug ?? {}),
        enabled:                  checked('debugEnabled'),
        frontendDevTools:         checked('debugFrontendDevTools'),
        openFrontendDevToolsOnStart: checked('debugOpenFrontendDevToolsOnStart'),
        captureFrontendConsole:   checked('debugCaptureFrontendConsole'),
        showBackendConsole:       checked('debugShowBackendConsole'),
        backendLogLevel:          get('debugBackendLogLevel') || 'info'
      },
      window: {
        ...original.window,
        title:     get('windowTitle'),
        width:     num('windowWidth'),
        height:    num('windowHeight'),
        minWidth:  num('windowMinWidth'),
        minHeight: num('windowMinHeight'),
        maxWidth:  num('windowMaxWidth'),
        maxHeight: num('windowMaxHeight'),
      },
      platforms: {
        ...original.platforms,
        windows: {
          archs: {
            ...(original.platforms?.windows?.archs ?? {}),
            x64: {
              ...(original.platforms?.windows?.archs?.x64 ?? {}),
              compileCommand:   get('winX64CompileCommand'),
              compileDirectory: get('winX64CompileDirectory'),
            }
          }
        }
      }
    };

    vscode.postMessage({ type: 'save', config: updated });
  });

  function populate(cfg) {
    const f   = document.getElementById('form');
    const set = (name, val) => { const el = f.elements[name]; if (el) el.value = val; };
    const setChecked = (name, val) => { const el = f.elements[name]; if (el) el.checked = !!val; };
    const debug = cfg.debug ?? {};
    renderPreBuildList(cfg.build?.preBuild ?? []);
    set('appName',                   cfg.appName);
    set('appVersion',                cfg.appVersion);
    set('organizationName',          cfg.organizationName);
    set('appIcon',                   cfg.appIcon);
    set('frontendInitialHTML',       cfg.frontendInitialHTML);
    set('developmentFrontendFolder', cfg.developmentFrontendFolder);
    set('developmentBackendFolder',  cfg.developmentBackendFolder);
    set('ipcTransport',              cfg.ipcTransport);
    set('resultFolder',              cfg.resultFolder);
    setChecked('debugEnabled', debug.enabled);
    setChecked('debugFrontendDevTools', debug.frontendDevTools);
    setChecked('debugOpenFrontendDevToolsOnStart', debug.openFrontendDevToolsOnStart);
    setChecked('debugCaptureFrontendConsole', debug.captureFrontendConsole);
    setChecked('debugShowBackendConsole', debug.showBackendConsole);
    set('debugBackendLogLevel', debug.backendLogLevel ?? 'info');
    set('windowTitle',     cfg.window.title);
    set('windowWidth',     cfg.window.width);
    set('windowHeight',    cfg.window.height);
    set('windowMinWidth',  cfg.window.minWidth);
    set('windowMinHeight', cfg.window.minHeight);
    set('windowMaxWidth',  cfg.window.maxWidth);
    set('windowMaxHeight', cfg.window.maxHeight);
    const x64 = cfg.platforms?.windows?.archs?.x64;
    if (x64) {
      set('winX64CompileCommand',   x64.compileCommand);
      set('winX64CompileDirectory', x64.compileDirectory);
    }
  }

  function renderPreBuildList(items) {
    const list = document.getElementById('preBuildList');
    list.innerHTML = '';
    (items && items.length ? items : []).forEach(item => appendPreBuildCommand(item));
  }

  function appendPreBuildCommand(item) {
    const list = document.getElementById('preBuildList');
    const row = document.createElement('div');
    row.className = 'prebuild-item';
    row.innerHTML = [
      '<div class="prebuild-row">',
      '  <div class="field">',
      '    <label>Name</label>',
      '    <input type="text" data-field="name">',
      '  </div>',
      '  <div class="field">',
      '    <label>Working Directory</label>',
      '    <input type="text" data-field="workingDirectory" placeholder="frontend">',
      '  </div>',
      '</div>',
      '<div class="field">',
      '  <label>Command</label>',
      '  <input type="text" data-field="command" placeholder="npm run build">',
      '</div>',
      '<div class="prebuild-actions">',
      '  <label class="check"><input type="checkbox" data-field="enabled"> Enabled</label>',
      '  <label class="check"><input type="checkbox" data-field="continueOnError"> Continue on error</label>',
      '  <button type="button" class="secondary" data-action="remove-prebuild">Remove</button>',
      '</div>'
    ].join('');

    row.querySelector('[data-field="name"]').value = item.name ?? '';
    row.querySelector('[data-field="command"]').value = item.command ?? '';
    row.querySelector('[data-field="workingDirectory"]').value = item.workingDirectory ?? '';
    row.querySelector('[data-field="enabled"]').checked = item.enabled !== false;
    row.querySelector('[data-field="continueOnError"]').checked = !!item.continueOnError;
    list.appendChild(row);
  }

  function collectPreBuild() {
    return Array.from(document.querySelectorAll('.prebuild-item'))
      .map(row => ({
        name: row.querySelector('[data-field="name"]').value,
        command: row.querySelector('[data-field="command"]').value,
        workingDirectory: row.querySelector('[data-field="workingDirectory"]').value,
        enabled: row.querySelector('[data-field="enabled"]').checked,
        continueOnError: row.querySelector('[data-field="continueOnError"]').checked
      }))
      .filter(item => item.command.trim() !== '' || item.workingDirectory.trim() !== '');
  }
</script>

</body>
</html>`;
}

function errorHtml(): string {
    return `<!DOCTYPE html><html><body>
        <p style="color:var(--vscode-errorForeground);padding:24px;font-family:var(--vscode-font-family)">
            config.json has a JSON syntax error — fix it in the text editor first.
        </p>
    </body></html>`;
}

function normalizePreBuild(build: BuildConfig | undefined): PreBuildCommandConfig[] {
    return build?.preBuild ?? [];
}

function renderPreBuildItems(items: PreBuildCommandConfig[]): string {
    return items.map(item => /* html */`
      <div class="prebuild-item">
        <div class="prebuild-row">
          <div class="field">
            <label>Name</label>
            <input type="text" data-field="name" value="${e(item.name ?? '')}">
          </div>
          <div class="field">
            <label>Working Directory</label>
            <input type="text" data-field="workingDirectory" value="${e(item.workingDirectory)}" placeholder="frontend">
          </div>
        </div>
        <div class="field">
          <label>Command</label>
          <input type="text" data-field="command" value="${e(item.command)}" placeholder="npm run build">
        </div>
        <div class="prebuild-actions">
          <label class="check"><input type="checkbox" data-field="enabled" ${item.enabled === false ? '' : 'checked'}> Enabled</label>
          <label class="check"><input type="checkbox" data-field="continueOnError" ${item.continueOnError ? 'checked' : ''}> Continue on error</label>
          <button type="button" class="secondary" data-action="remove-prebuild">Remove</button>
        </div>
      </div>`).join('');
}

function normalizeDebug(debug: DebugConfig | undefined): DebugConfig {
    return {
        enabled:                  debug?.enabled ?? false,
        frontendDevTools:         debug?.frontendDevTools ?? false,
        openFrontendDevToolsOnStart: debug?.openFrontendDevToolsOnStart ?? false,
        captureFrontendConsole:   debug?.captureFrontendConsole ?? false,
        showBackendConsole:       debug?.showBackendConsole ?? false,
        backendLogLevel:          debug?.backendLogLevel ?? 'info'
    };
}

function e(value: unknown): string {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}
