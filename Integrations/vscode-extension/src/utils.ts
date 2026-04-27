import * as vscode from 'vscode';
import * as fs     from 'fs';
import * as path   from 'path';

export function resolveFrameworkPath(): string | null {
    const setting = vscode.workspace.getConfiguration('lambdaflow').get<string>('frameworkPath');
    if (typeof setting === 'string' && setting.trim().length > 0) return setting.trim();

    const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!root) return null;

    const indicator = path.join(root, 'lambdaflow', 'Hosts', 'Windows', 'lambdaflow.windows.csproj');
    return fs.existsSync(indicator) ? root : null;
}

export function cliProjectPath(frameworkRoot: string): string {
    return path.join(frameworkRoot, 'lambdaflow', 'Tools', 'LambdaFlow.Cli', 'LambdaFlow.Cli.csproj');
}
