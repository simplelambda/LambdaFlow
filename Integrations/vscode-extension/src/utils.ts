import * as vscode from 'vscode';
import * as fs     from 'fs';
import * as os     from 'os';
import * as path   from 'path';

export function resolveFrameworkPath(requireProjectTemplates = false): string | null {
    const setting = vscode.workspace.getConfiguration('lambdaflow').get<string>('frameworkPath');
    if (typeof setting === 'string' && setting.trim().length > 0) {
        const configured = normalizeUserPath(setting);
        if (isFrameworkSourceRoot(configured, requireProjectTemplates))
            return configured;
    }

    const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!root) return null;

    return isFrameworkSourceRoot(root, requireProjectTemplates) ? root : null;
}

export function cliProjectPath(frameworkRoot: string): string {
    return path.join(frameworkRoot, 'lambdaflow', 'Tools', 'LambdaFlow.Cli', 'LambdaFlow.Cli.csproj');
}

export function isFrameworkSourceRoot(
    frameworkRoot: string,
    requireProjectTemplates = false
): boolean {
    if (!fs.existsSync(cliProjectPath(frameworkRoot)))
        return false;
    if (!requireProjectTemplates)
        return true;

    return [
        path.join(frameworkRoot, 'Examples', 'CSharp', 'backend', 'Backend.csproj'),
        path.join(frameworkRoot, 'lambdaflow', 'Sdk', 'JavaScript', 'lambdaflow.js'),
        path.join(frameworkRoot, 'lambdaflow', 'Sdk', 'CSharp', 'LambdaFlow.cs')
    ].every(candidate => fs.existsSync(candidate));
}

export function resolveDotnetPath(): string | null {
    const setting = vscode.workspace.getConfiguration('lambdaflow').get<string>('dotnetPath');
    if (typeof setting === 'string' && setting.trim().length > 0) {
        const configured = normalizeUserPath(setting);
        if (isExecutableFile(configured))
            return configured;
    }

    const executable = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
    const candidates = [
        process.env['DOTNET_HOST_PATH'],
        process.env['DOTNET_ROOT'] ? path.join(process.env['DOTNET_ROOT'], executable) : undefined,
        ...String(process.env['PATH'] ?? '')
            .split(path.delimiter)
            .filter(Boolean)
            .map(directory => path.join(directory, executable)),
        process.platform === 'win32'
            ? path.join(process.env['ProgramFiles'] ?? 'C:\\Program Files', 'dotnet', executable)
            : '/usr/bin/dotnet',
        process.platform === 'win32'
            ? path.join(process.env['ProgramFiles(x86)'] ?? 'C:\\Program Files (x86)', 'dotnet', executable)
            : '/usr/local/bin/dotnet',
        process.platform === 'linux' ? '/usr/share/dotnet/dotnet' : undefined,
        path.join(os.homedir(), '.dotnet', executable)
    ];

    return candidates.find((candidate): candidate is string =>
        typeof candidate === 'string' && isExecutableFile(candidate)
    ) ?? null;
}

export function resolveNodePath(): string | null {
    const setting = vscode.workspace.getConfiguration('lambdaflow').get<string>('nodePath');
    if (typeof setting === 'string' && setting.trim().length > 0) {
        const configured = normalizeUserPath(setting);
        if (isExecutableFile(configured))
            return configured;
    }

    const executable = process.platform === 'win32' ? 'node.exe' : 'node';
    const candidates = [
        process.env['NODE'],
        ...String(process.env['PATH'] ?? '')
            .split(path.delimiter)
            .filter(Boolean)
            .map(directory => path.join(directory, executable)),
        process.platform === 'win32'
            ? path.join(process.env['ProgramFiles'] ?? 'C:\\Program Files', 'nodejs', executable)
            : '/usr/bin/node',
        process.platform === 'win32'
            ? undefined
            : '/usr/local/bin/node',
        path.join(os.homedir(), '.local', 'share', 'node', 'bin', executable),
        path.join(os.homedir(), '.local', 'node', 'bin', executable)
    ];

    return candidates.find((candidate): candidate is string =>
        typeof candidate === 'string' && isExecutableFile(candidate)
    ) ?? null;
}

function normalizeUserPath(value: string): string {
    const trimmed = value.trim();
    const expanded = trimmed === '~'
        ? os.homedir()
        : trimmed.startsWith('~/') || trimmed.startsWith('~\\')
            ? path.join(os.homedir(), trimmed.slice(2))
            : trimmed;

    return path.resolve(expanded);
}

function isExecutableFile(candidate: string): boolean {
    try {
        const stats = fs.statSync(candidate);
        if (!stats.isFile()) return false;

        if (process.platform !== 'win32')
            fs.accessSync(candidate, fs.constants.X_OK);

        return true;
    } catch {
        return false;
    }
}
