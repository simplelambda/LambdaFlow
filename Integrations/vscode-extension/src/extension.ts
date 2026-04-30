import * as vscode from 'vscode';
import * as path   from 'path';
import * as fs     from 'fs';
import * as os     from 'os';
import * as cp     from 'child_process';
import { LambdaFlowConfigEditorProvider } from './ConfigEditorPanel';
import { SidebarProvider }               from './SidebarProvider';
import { resolveFrameworkPath, cliProjectPath } from './utils';

const REPO_URL = 'https://github.com/simplelambda/LambdaFlow.git';

interface LanguageTemplate {
    label:    string;
    cliValue: 'csharp' | 'java' | 'python' | 'other';
    detail:   string;
}

interface LanguageTemplatePickItem extends vscode.QuickPickItem {
    template: LanguageTemplate;
}

interface FrontendTemplate {
    label:    string;
    cliValue: 'basic' | 'react';
    detail:   string;
}

interface FrontendTemplatePickItem extends vscode.QuickPickItem {
    template: FrontendTemplate;
}

const LANGUAGE_TEMPLATES: LanguageTemplate[] = [
    { label: 'C#',     cliValue: 'csharp',  detail: '.NET / C# backend'  },
    { label: 'Java',   cliValue: 'java',    detail: 'Maven / Java backend' },
    { label: 'Python', cliValue: 'python',  detail: 'Python backend'      },
    { label: 'Other',  cliValue: 'other',   detail: 'Generic backend command configured manually' }
];

const FRONTEND_TEMPLATES: FrontendTemplate[] = [
    { label: 'HTML basic', cliValue: 'basic', detail: 'Plain HTML/CSS/JS frontend' },
    { label: 'React',      cliValue: 'react', detail: 'Vite React frontend with npm pre-build' }
];

export function activate(context: vscode.ExtensionContext): void {
    const sidebar = new SidebarProvider(context);

    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(SidebarProvider.viewId, sidebar),
        LambdaFlowConfigEditorProvider.register(context),
        vscode.commands.registerCommand('lambdaflow.newProject',   () => cmdNewProject()),
        vscode.commands.registerCommand('lambdaflow.buildProject', () => cmdBuildProject()),
        vscode.commands.registerCommand('lambdaflow.runProject',   () => cmdRunProject(false)),
        vscode.commands.registerCommand('lambdaflow.debugProject', () => cmdRunProject(true)),
        vscode.commands.registerCommand('lambdaflow.openConfig',   () => cmdOpenConfig())
    );
}

export function deactivate(): void {}

// ---------------------------------------------------------------------------

async function cmdNewProject(): Promise<void> {
    const frameworkPath = await requireFrameworkPath();
    if (!frameworkPath) return;

    const appName = await vscode.window.showInputBox({
        title:         'LambdaFlow — New Project',
        prompt:        'Application name',
        placeHolder:   'MyApp',
        validateInput: v => v.trim() === '' ? 'App name is required.' : undefined
    });
    if (!appName) return;

    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    const defaultDir    = workspaceRoot
        ? path.join(workspaceRoot, 'Apps', appName)
        : appName;

    const targetDir = await vscode.window.showInputBox({
        title: 'LambdaFlow — New Project',
        prompt: 'Target directory (will be created)',
        value:  defaultDir
    });
    if (!targetDir) return;

    const template = await pickLanguageTemplate();
    if (!template) return;

    const frontend = await pickFrontendTemplate();
    if (!frontend) return;

    const cli      = cliProjectPath(frameworkPath);
    const terminal = vscode.window.createTerminal({ name: 'LambdaFlow' });
    terminal.show();
    terminal.sendText(
        `dotnet run --project ${q(cli)} -- new ${q(appName)} ${q(targetDir)} --framework ${q(frameworkPath)} --language ${q(template.cliValue)} --frontend ${q(frontend.cliValue)} --self-contained`
    );

    const action = await vscode.window.showInformationMessage(
        `Creating "${appName}" (${template.label}, ${frontend.label}) at ${targetDir}. Open when the terminal finishes.`,
        'Open Folder'
    );
    if (action === 'Open Folder') {
        vscode.commands.executeCommand('vscode.openFolder', vscode.Uri.file(targetDir), { forceNewWindow: true });
    }
}

async function cmdBuildProject(): Promise<void> {
    const projectDir = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!projectDir) {
        vscode.window.showErrorMessage('LambdaFlow: No workspace folder is open.');
        return;
    }

    if (!fs.existsSync(path.join(projectDir, 'config.json'))) {
        vscode.window.showErrorMessage('LambdaFlow: config.json not found. Open a LambdaFlow project folder.');
        return;
    }

    const frameworkPath = await requireFrameworkPath();
    if (!frameworkPath) return;

    const cli      = cliProjectPath(frameworkPath);
    const terminal = vscode.window.createTerminal({ name: 'LambdaFlow Build' });
    terminal.show();
    terminal.sendText(
        `dotnet run --project "${cli}" -- build "${projectDir}" --framework "${frameworkPath}"`
    );
}

async function cmdRunProject(forceDebug: boolean): Promise<void> {
    const projectDir = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!projectDir) {
        vscode.window.showErrorMessage('LambdaFlow: No workspace folder is open.');
        return;
    }

    const configPath = path.join(projectDir, 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage('LambdaFlow: config.json not found. Open a LambdaFlow project folder.');
        return;
    }

    const frameworkPath = await requireFrameworkPath();
    if (!frameworkPath) return;

    let cfg: { appName?: unknown; appVersion?: unknown };
    try   { cfg = JSON.parse(fs.readFileSync(configPath, 'utf8')); }
    catch { vscode.window.showErrorMessage('LambdaFlow: Failed to parse config.json.'); return; }

    const appName    = String(cfg.appName    ?? 'App');
    const appVersion = String(cfg.appVersion ?? '1.0.0');
    const sanitized  = sanitizeFileName(appName);
    const appDir     = path.join(projectDir, 'Results', `${sanitized}-${sanitizeFileName(appVersion)}`, 'windows-x64');
    const exePath    = path.join(appDir, `${sanitized}.exe`);

    const cli       = cliProjectPath(frameworkPath);
    const debugArg  = forceDebug ? ' --debug' : '';
    const buildTask = new vscode.Task(
        { type: 'shell', task: 'LambdaFlow: build' },
        vscode.workspace.workspaceFolders![0],
        forceDebug ? 'LambdaFlow: build debug app' : 'LambdaFlow: build app',
        'LambdaFlow',
        new vscode.ShellExecution(
            `dotnet run --project "${cli}" -- build "${projectDir}" --framework "${frameworkPath}"${debugArg}`
        )
    );

    const execution = await vscode.tasks.executeTask(buildTask);

    try {
        await new Promise<void>((resolve, reject) => {
            const disposable = vscode.tasks.onDidEndTaskProcess(e => {
                if (e.execution === execution) {
                    disposable.dispose();
                    if (e.exitCode === 0) resolve();
                    else reject(new Error(`Build failed (exit code ${e.exitCode ?? '?'})`));
                }
            });
        });
    } catch (err) {
        vscode.window.showErrorMessage(`LambdaFlow: ${err instanceof Error ? err.message : String(err)}`);
        return;
    }

    if (!fs.existsSync(exePath)) {
        vscode.window.showErrorMessage(`LambdaFlow: Executable not found at ${exePath}`);
        return;
    }

    cp.spawn(exePath, [], { detached: true, stdio: 'ignore', cwd: appDir }).unref();
    vscode.window.showInformationMessage(`LambdaFlow: ${appName} started${forceDebug ? ' in debug mode' : ''}.`);
}

async function cmdOpenConfig(): Promise<void> {
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!workspaceRoot) {
        vscode.window.showErrorMessage('LambdaFlow: No workspace folder is open.');
        return;
    }

    const configPath = path.join(workspaceRoot, 'config.json');
    if (!fs.existsSync(configPath)) {
        vscode.window.showErrorMessage('LambdaFlow: config.json not found. Open a LambdaFlow project folder.');
        return;
    }

    vscode.commands.executeCommand(
        'vscode.openWith',
        vscode.Uri.file(configPath),
        LambdaFlowConfigEditorProvider.viewType
    );
}

// ---------------------------------------------------------------------------

async function requireFrameworkPath(): Promise<string | undefined> {
    const resolved = resolveFrameworkPath();
    if (resolved) return resolved;

    const action = await vscode.window.showInformationMessage(
        'LambdaFlow: framework not found. Download it automatically?',
        'Download',
        'Set Path Manually'
    );
    if (action === 'Download')        return downloadFramework();
    if (action === 'Set Path Manually')
        vscode.commands.executeCommand('workbench.action.openSettings', 'lambdaflow.frameworkPath');
    return undefined;
}

async function downloadFramework(): Promise<string | undefined> {
    const appData   = process.env['APPDATA'] ?? path.join(os.homedir(), 'AppData', 'Roaming');
    const targetDir = path.join(appData, 'LambdaFlow', 'framework');
    const indicator = path.join(targetDir, 'lambdaflow', 'Hosts', 'Windows', 'lambdaflow.windows.csproj');

    if (fs.existsSync(indicator)) {
        await vscode.workspace.getConfiguration('lambdaflow')
            .update('frameworkPath', targetDir, vscode.ConfigurationTarget.Global);
        return targetDir;
    }

    return vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'LambdaFlow: Downloading framework…', cancellable: false },
        async () => {
            try {
                fs.mkdirSync(path.dirname(targetDir), { recursive: true });
                if (fs.existsSync(targetDir))
                    fs.rmSync(targetDir, { recursive: true, force: true });

                await new Promise<void>((resolve, reject) => {
                    const child = cp.spawn('git', ['clone', '--depth=1', REPO_URL, targetDir], { stdio: 'pipe' });
                    child.on('close', code => (code === 0 ? resolve() : reject(new Error(`git clone exited ${code}`))));
                    child.on('error', reject);
                });

                await vscode.workspace.getConfiguration('lambdaflow')
                    .update('frameworkPath', targetDir, vscode.ConfigurationTarget.Global);
                vscode.window.showInformationMessage(`LambdaFlow framework downloaded to ${targetDir}`);
                return targetDir;
            } catch (err) {
                vscode.window.showErrorMessage(
                    `LambdaFlow: Failed to download framework — ${err instanceof Error ? err.message : String(err)}`
                );
                return undefined;
            }
        }
    );
}

async function pickLanguageTemplate(): Promise<LanguageTemplate | undefined> {
    const items: LanguageTemplatePickItem[] = LANGUAGE_TEMPLATES.map(template => ({
        label:    template.label,
        detail:   template.detail,
        template
    }));

    const selected = await vscode.window.showQuickPick(items, {
        title:       'LambdaFlow — New Project',
        placeHolder: 'Choose backend language'
    });

    return selected?.template;
}

async function pickFrontendTemplate(): Promise<FrontendTemplate | undefined> {
    const items: FrontendTemplatePickItem[] = FRONTEND_TEMPLATES.map(template => ({
        label:    template.label,
        detail:   template.detail,
        template
    }));

    const selected = await vscode.window.showQuickPick(items, {
        title:       'LambdaFlow â€” New Project',
        placeHolder: 'Choose frontend type'
    });

    return selected?.template;
}

async function pickDebugMode(): Promise<boolean | undefined> {
    const selected = await vscode.window.showQuickPick(
        [
            {
                label: 'No',
                description: 'Normal mode',
                value: false
            },
            {
                label: 'Yes',
                description: 'Enable DevTools, console capture, and backend debug logs',
                value: true
            }
        ],
        {
            title:       'LambdaFlow â€” New Project',
            placeHolder: 'Enable debug mode during development?'
        }
    );

    return selected?.value;
}

function sanitizeFileName(value: string): string {
    return value.replace(/[<>:"/\\|?*]/g, '-');
}

function q(value: string): string {
    return `"${value}"`;
}
