import * as vscode from 'vscode';
import * as path   from 'path';
import * as fs     from 'fs';
import { LambdaFlowConfigEditorProvider } from './ConfigEditorPanel';
import { SidebarProvider }               from './SidebarProvider';
import { resolveFrameworkPath, cliProjectPath } from './utils';

interface LanguageTemplate {
    label:            string;
    cliValue:         'csharp' | 'java' | 'python';
    compileCommand:   string;
    compileDirectory: string;
}

interface LanguageTemplatePickItem extends vscode.QuickPickItem {
    template: LanguageTemplate;
}

const LANGUAGE_TEMPLATES: LanguageTemplate[] = [
    {
        label:            'C#',
        cliValue:         'csharp',
        compileCommand:   'dotnet publish Backend.csproj -c Release -r win-x64 --self-contained false -o bin',
        compileDirectory: 'bin'
    },
    {
        label:            'Java',
        cliValue:         'java',
        compileCommand:   'mvn -q -DskipTests package',
        compileDirectory: 'target'
    },
    {
        label:            'Python',
        cliValue:         'python',
        compileCommand:   'python build.py',
        compileDirectory: 'bin'
    }
];

export function activate(context: vscode.ExtensionContext): void {
    const sidebar = new SidebarProvider(context);

    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(SidebarProvider.viewId, sidebar),
        LambdaFlowConfigEditorProvider.register(context),
        vscode.commands.registerCommand('lambdaflow.newProject',   () => cmdNewProject()),
        vscode.commands.registerCommand('lambdaflow.buildProject', () => cmdBuildProject()),
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

    const template = await pickLanguageTemplate();
    if (!template) return;

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

    const compileCommand = await vscode.window.showInputBox({
        title:       'LambdaFlow — New Project',
        prompt:      `Backend compile command (${template.label})`,
        value:       template.compileCommand,
        validateInput: v => {
            if (v.trim() === '') return 'Compile command is required.';
            if (v.includes('"')) return 'Double quotes are not supported in this prompt.';
            return undefined;
        }
    });
    if (!compileCommand) return;

    const compileDirectory = await vscode.window.showInputBox({
        title:       'LambdaFlow — New Project',
        prompt:      `Backend compile output directory (${template.label})`,
        value:       template.compileDirectory,
        validateInput: v => {
            if (v.trim() === '') return 'Compile directory is required.';
            if (v.includes('"')) return 'Double quotes are not supported in this prompt.';
            return undefined;
        }
    });
    if (!compileDirectory) return;

    const cli      = cliProjectPath(frameworkPath);
    const terminal = vscode.window.createTerminal({ name: 'LambdaFlow' });
    terminal.show();
    const command = [
        `dotnet run --project ${q(cli)} -- new ${q(appName)} ${q(targetDir)} --framework ${q(frameworkPath)}`,
        `--language ${q(template.cliValue)}`,
        `--backend-compile-command ${q(compileCommand)}`,
        `--backend-compile-directory ${q(compileDirectory)}`,
        '--self-contained'
    ].join(' ');
    terminal.sendText(
        command
    );

    const action = await vscode.window.showInformationMessage(
        `Creating "${appName}" (${template.label}) at ${targetDir}. Open when the terminal finishes.`,
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

    const action = await vscode.window.showErrorMessage(
        'LambdaFlow: framework not found. Set "lambdaflow.frameworkPath" in Settings, or open a self-contained project.',
        'Open Settings'
    );
    if (action === 'Open Settings') {
        vscode.commands.executeCommand('workbench.action.openSettings', 'lambdaflow.frameworkPath');
    }
    return undefined;
}

async function pickLanguageTemplate(): Promise<LanguageTemplate | undefined> {
    const items: LanguageTemplatePickItem[] = LANGUAGE_TEMPLATES.map(template => ({
        label:       template.label,
        description: 'Backend template language',
        detail:      `Compile: ${template.compileCommand} | Output: ${template.compileDirectory}`,
        template
    }));

    const selected = await vscode.window.showQuickPick(items, {
        title:       'LambdaFlow — New Project',
        placeHolder: 'Choose backend language template'
    });

    return selected?.template;
}

function q(value: string): string {
    return `"${value}"`;
}
