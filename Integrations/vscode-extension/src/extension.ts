import * as vscode from 'vscode';
import * as path   from 'path';
import * as fs     from 'fs';
import * as os     from 'os';
import * as cp     from 'child_process';
import { LambdaFlowConfigEditorProvider } from './ConfigEditorPanel';
import { SidebarProvider }               from './SidebarProvider';
import {
    resolveFrameworkPath,
    resolveDotnetPath,
    resolveNodePath,
    cliProjectPath,
    isFrameworkSourceRoot
} from './utils';

const REPO_URL = 'https://github.com/simplelambda/LambdaFlow.git';

interface LanguageTemplate {
    label:    string;
    cliValue: 'csharp' | 'java' | 'python' | 'node' | 'go' | 'other';
    detail:   string;
}

interface LanguageTemplatePickItem extends vscode.QuickPickItem {
    template: LanguageTemplate;
}

interface FrontendTemplate {
    label:    string;
    cliValue: 'basic' | 'react' | 'vue' | 'svelte';
    detail:   string;
}

interface FrontendTemplatePickItem extends vscode.QuickPickItem {
    template: FrontendTemplate;
}

const LANGUAGE_TEMPLATES: LanguageTemplate[] = [
    { label: 'C#',     cliValue: 'csharp',  detail: '.NET / C# backend'  },
    { label: 'Java',   cliValue: 'java',    detail: 'Maven / Java backend' },
    { label: 'Python', cliValue: 'python',  detail: 'Python backend'      },
    { label: 'Node.js', cliValue: 'node',   detail: 'Dependency-free JavaScript backend starter' },
    { label: 'Go',      cliValue: 'go',     detail: 'Cross-compiled native Go backend starter' },
    { label: 'Other',  cliValue: 'other',   detail: 'Generic backend command configured manually' }
];

const FRONTEND_TEMPLATES: FrontendTemplate[] = [
    { label: 'HTML basic', cliValue: 'basic', detail: 'Plain HTML/CSS/JS frontend' },
    { label: 'React',      cliValue: 'react', detail: 'Vite + React frontend with backend connectivity check' },
    { label: 'Vue',        cliValue: 'vue',   detail: 'Vite + Vue frontend with backend connectivity check' },
    { label: 'Svelte',     cliValue: 'svelte', detail: 'Vite + Svelte frontend with backend connectivity check' }
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
    const frameworkPath = await requireFrameworkPath(true);
    if (!frameworkPath) return;
    const dotnetPath = await requireDotnetPath();
    if (!dotnetPath) return;

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
        : path.join(os.homedir(), appName);

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

    const cli = cliProjectPath(frameworkPath);
    const succeeded = await runCliTask(
        'LambdaFlow: create project',
        dotnetPath,
        cli,
        [
            'new',
            appName,
            targetDir,
            '--framework',
            frameworkPath,
            '--language',
            template.cliValue,
            '--frontend',
            frontend.cliValue,
            '--self-contained'
        ],
        frameworkPath
    );
    if (!succeeded) return;

    const action = await vscode.window.showInformationMessage(
        `LambdaFlow: "${appName}" (${template.label}, ${frontend.label}) was created at ${targetDir}.`,
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
    const dotnetPath = await requireDotnetPath();
    if (!dotnetPath) return;

    const succeeded = await runCliTask(
        'LambdaFlow: build app',
        dotnetPath,
        cliProjectPath(frameworkPath),
        ['build', projectDir, '--framework', frameworkPath],
        projectDir
    );
    if (succeeded)
        vscode.window.showInformationMessage('LambdaFlow: Build completed.');
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
    const dotnetPath = await requireDotnetPath();
    if (!dotnetPath) return;

    let cfg: { appName?: unknown; appVersion?: unknown; resultFolder?: unknown };
    try   { cfg = JSON.parse(fs.readFileSync(configPath, 'utf8')); }
    catch { vscode.window.showErrorMessage('LambdaFlow: Failed to parse config.json.'); return; }

    const target = currentTarget();
    if (!target) {
        vscode.window.showErrorMessage(`LambdaFlow: ${process.platform}/${process.arch} is not a supported host.`);
        return;
    }

    const appName    = String(cfg.appName    ?? 'App');
    const appVersion = String(cfg.appVersion ?? '1.0.0');
    const resultFolder = String(cfg.resultFolder ?? 'Results');
    const sanitized  = sanitizeFileName(appName);
    const appDir     = path.join(projectDir, resultFolder, `${sanitized}-${sanitizeFileName(appVersion)}`, target.name);
    const exePath    = path.join(appDir, `${sanitized}${target.extension}`);

    const buildArgs = ['build', projectDir, '--framework', frameworkPath];
    if (forceDebug) buildArgs.push('--debug');

    const succeeded = await runCliTask(
        forceDebug ? 'LambdaFlow: build debug app' : 'LambdaFlow: build app',
        dotnetPath,
        cliProjectPath(frameworkPath),
        buildArgs,
        projectDir
    );
    if (!succeeded) return;

    if (!fs.existsSync(exePath)) {
        vscode.window.showErrorMessage(`LambdaFlow: Executable not found at ${exePath}`);
        return;
    }

    try {
        const app = cp.spawn(exePath, [], {
            detached: true,
            stdio: 'ignore',
            cwd: appDir,
            env: cliEnvironment(dotnetPath)
        });
        await waitForStableSpawn(app);
        app.unref();
        vscode.window.showInformationMessage(
            `LambdaFlow: ${appName} started${forceDebug ? ' in debug mode' : ''}.`
        );
    } catch (err) {
        vscode.window.showErrorMessage(
            `LambdaFlow: Failed to start ${appName} — ${err instanceof Error ? err.message : String(err)}`
        );
    }
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

async function requireFrameworkPath(requireProjectTemplates = false): Promise<string | undefined> {
    const resolved = resolveFrameworkPath(requireProjectTemplates);
    if (resolved) return resolved;

    const action = await vscode.window.showInformationMessage(
        'LambdaFlow: framework source not found. Download the public repository or select an existing copy.',
        'Download Framework',
        'Select Existing',
        'Open Settings'
    );
    if (action === 'Download Framework') return chooseFrameworkDownloadLocation();
    if (action === 'Select Existing') return selectExistingFramework();
    if (action === 'Open Settings')
        await vscode.commands.executeCommand('workbench.action.openSettings', 'lambdaflow.frameworkPath');
    return undefined;
}

async function requireDotnetPath(): Promise<string | undefined> {
    const resolved = resolveDotnetPath();
    if (resolved) return resolved;

    const action = await vscode.window.showErrorMessage(
        'LambdaFlow: .NET 8 SDK was not found. Install it or set an absolute path in LambdaFlow: Dotnet Path.',
        'Installation Guide',
        'Set Path Manually'
    );

    if (action === 'Installation Guide')
        await vscode.env.openExternal(vscode.Uri.parse('https://dotnet.microsoft.com/download/dotnet/8.0'));
    if (action === 'Set Path Manually')
        await vscode.commands.executeCommand('workbench.action.openSettings', 'lambdaflow.dotnetPath');

    return undefined;
}

async function runCliTask(
    name: string,
    dotnetPath: string,
    cliPath: string,
    cliArgs: string[],
    cwd: string
): Promise<boolean> {
    const scope = vscode.workspace.workspaceFolders?.[0] ?? vscode.TaskScope.Global;
    const task = new vscode.Task(
        { type: 'lambdaflow', task: name },
        scope,
        name,
        'LambdaFlow',
        new vscode.ProcessExecution(
            dotnetPath,
            ['run', '--project', cliPath, '--', ...cliArgs],
            {
                cwd,
                env: cliEnvironment(dotnetPath)
            }
        )
    );
    task.presentationOptions = {
        reveal: vscode.TaskRevealKind.Always,
        panel: vscode.TaskPanelKind.Shared,
        clear: true
    };

    try {
        const execution = await vscode.tasks.executeTask(task);
        const exitCode = await new Promise<number | undefined>(resolve => {
            const disposable = vscode.tasks.onDidEndTaskProcess(event => {
                if (event.execution !== execution) return;
                disposable.dispose();
                resolve(event.exitCode);
            });
        });

        if (exitCode === 0) return true;

        vscode.window.showErrorMessage(
            `LambdaFlow: ${name} failed (exit code ${exitCode ?? 'unknown'}).`
        );
        return false;
    } catch (err) {
        vscode.window.showErrorMessage(
            `LambdaFlow: ${name} could not start — ${err instanceof Error ? err.message : String(err)}`
        );
        return false;
    }
}

function cliEnvironment(dotnetPath: string): Record<string, string> {
    const nodePath = resolveNodePath();
    const executableDirectories = [
        path.dirname(dotnetPath),
        nodePath ? path.dirname(nodePath) : undefined
    ].filter((directory): directory is string => Boolean(directory));
    const pathKey = Object.keys(process.env)
        .find(key => key.toLowerCase() === 'path') ?? 'PATH';
    const inheritedPath = process.env[pathKey] ?? '';

    return {
        ...Object.fromEntries(
            Object.entries(process.env).filter((entry): entry is [string, string] =>
                typeof entry[1] === 'string'
            )
        ),
        [pathKey]: [...executableDirectories, inheritedPath].filter(Boolean).join(path.delimiter)
    };
}

function waitForStableSpawn(child: cp.ChildProcess, stabilityMs = 750): Promise<void> {
    return new Promise((resolve, reject) => {
        let timer: NodeJS.Timeout | undefined;
        const cleanup = () => {
            if (timer) clearTimeout(timer);
            child.off('spawn', onSpawn);
            child.off('error', onError);
            child.off('exit', onExit);
        };
        const onSpawn = () => {
            timer = setTimeout(() => {
                cleanup();
                resolve();
            }, stabilityMs);
        };
        const onError = (error: Error) => {
            cleanup();
            reject(error);
        };
        const onExit = (code: number | null, signal: NodeJS.Signals | null) => {
            cleanup();
            reject(new Error(
                `application exited during startup (${signal ? `signal ${signal}` : `code ${code ?? 'unknown'}`}); check lambdaflow.crash.log`
            ));
        };

        child.once('spawn', onSpawn);
        child.once('error', onError);
        child.once('exit', onExit);
    });
}

function recommendedFrameworkPath(): string {
    const appData   = process.platform === 'win32'
        ? (process.env['APPDATA'] ?? path.join(os.homedir(), 'AppData', 'Roaming'))
        : (process.env['XDG_DATA_HOME'] ?? path.join(os.homedir(), '.local', 'share'));
    return path.join(appData, 'LambdaFlow', 'framework');
}

async function chooseFrameworkDownloadLocation(): Promise<string | undefined> {
    const recommended = recommendedFrameworkPath();
    const choice = await vscode.window.showQuickPick(
        [
            {
                label: 'Recommended location',
                description: recommended,
                target: recommended
            },
            {
                label: 'Choose parent folder…',
                description: 'Clone into a LambdaFlow subfolder',
                target: null
            }
        ],
        {
            title: 'LambdaFlow — Framework Location',
            placeHolder: 'Choose where the public repository should be cloned'
        }
    );
    if (!choice) return undefined;
    if (choice.target) return downloadFramework(choice.target);

    const selected = await vscode.window.showOpenDialog({
        title: 'Choose the parent folder for the LambdaFlow repository',
        defaultUri: vscode.Uri.file(path.dirname(recommended)),
        openLabel: 'Clone Here',
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: false
    });
    if (!selected?.[0]) return undefined;

    return downloadFramework(path.join(selected[0].fsPath, 'LambdaFlow'));
}

async function selectExistingFramework(): Promise<string | undefined> {
    const selected = await vscode.window.showOpenDialog({
        title: 'Select an existing LambdaFlow repository',
        openLabel: 'Use Framework',
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: false
    });
    if (!selected?.[0]) return undefined;

    const targetDir = selected[0].fsPath;
    if (!isFrameworkSourceRoot(targetDir, true)) {
        vscode.window.showErrorMessage(
            `LambdaFlow: ${targetDir} is not a complete framework repository. Select the cloned repository root containing the CLI, SDKs, and Examples folders.`
        );
        return undefined;
    }

    await vscode.workspace.getConfiguration('lambdaflow')
        .update('frameworkPath', targetDir, vscode.ConfigurationTarget.Global);
    return targetDir;
}

async function downloadFramework(targetDir: string): Promise<string | undefined> {
    if (isFrameworkSourceRoot(targetDir, true)) {
        await vscode.workspace.getConfiguration('lambdaflow')
            .update('frameworkPath', targetDir, vscode.ConfigurationTarget.Global);
        return targetDir;
    }

    return vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'LambdaFlow: Downloading framework…', cancellable: false },
        async () => {
            try {
                fs.mkdirSync(path.dirname(targetDir), { recursive: true });
                if (fs.existsSync(targetDir)) {
                    vscode.window.showErrorMessage(
                        `LambdaFlow: ${targetDir} already exists but is not a valid framework repository. Choose another location or select a valid existing copy.`
                    );
                    return undefined;
                }

                const temporaryDir = `${targetDir}.download-${process.pid}-${Date.now()}`;
                try {
                    await new Promise<void>((resolve, reject) => {
                        const child = cp.spawn(
                            'git',
                            ['clone', '--depth=1', REPO_URL, temporaryDir],
                            { stdio: ['ignore', 'ignore', 'pipe'] }
                        );
                        let stderr = '';
                        child.stderr?.on('data', chunk => {
                            stderr += String(chunk);
                            if (stderr.length > 4000) stderr = stderr.slice(-4000);
                        });
                        child.on('close', code => code === 0
                            ? resolve()
                            : reject(new Error(stderr.trim() || `git clone exited ${code}`)));
                        child.on('error', reject);
                    });

                    if (!isFrameworkSourceRoot(temporaryDir, true))
                        throw new Error('Downloaded repository does not contain the LambdaFlow CLI, SDKs, and project templates.');

                    fs.renameSync(temporaryDir, targetDir);
                } catch (err) {
                    if (fs.existsSync(temporaryDir))
                        fs.rmSync(temporaryDir, { recursive: true, force: true });
                    throw err;
                }

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
        title:       'LambdaFlow — New Project',
        placeHolder: 'Choose frontend type'
    });

    return selected?.template;
}

function sanitizeFileName(value: string): string {
    let sanitized = value.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').replace(/[ .]+$/g, '');
    if (!sanitized.trim()) sanitized = 'LambdaFlowApp';

    const stem = sanitized.split('.', 1)[0].toUpperCase();
    if (/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/.test(stem))
        sanitized = `_${sanitized}`;

    return sanitized;
}

function currentTarget(): { name: string; extension: string } | null {
    const arch = process.arch === 'x64'
        ? 'x64'
        : process.arch === 'arm64'
            ? 'arm64'
            : null;
    if (!arch) return null;

    if (process.platform === 'win32')
        return { name: `windows-${arch}`, extension: '.exe' };
    if (process.platform === 'linux')
        return { name: `linux-${arch}`, extension: '' };
    return null;
}
