using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace lambdaflow.lambdaflow.Core{
    internal class BackendProcess : IDisposable{
        #region Variables

            private readonly Process _process;
            private static readonly object LogLock = new object();

            internal event Func<string, Task>? OnStdOut;
            internal event Func<string, Task>? OnStdErr;

            internal bool HasExited => _process.HasExited;

        #endregion

        #region Constructors

            internal BackendProcess() : this(CreateDefaultStartInfo()) { }

            internal BackendProcess(ProcessStartInfo startInfo) {
                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += StdOutHandler;
                _process.ErrorDataReceived += StdErrHandler;

                _process.Start();

                if (_process.StartInfo.RedirectStandardOutput)
                    _process.BeginOutputReadLine();

                if (_process.StartInfo.RedirectStandardError)
                    _process.BeginErrorReadLine();

                if (_process.StartInfo.RedirectStandardInput)
                    _process.StandardInput.AutoFlush = true;
            }

        #endregion

        #region Public methods

            public void Dispose() {
                try {
                    if (!_process.HasExited) {
                        try { _process.StandardInput.Close(); } catch { }
                        if (!_process.WaitForExit(2000)) _process.Kill();
                    }
                }
                catch { }
                finally {
                    _process.Dispose();
                }
            }

        #endregion

        #region Internal methods

            internal Task WriteLineAsync(string line, CancellationToken ct = default) {
                if (line is null) throw new ArgumentNullException(nameof(line));
                if (!_process.StartInfo.RedirectStandardInput)
                    throw new InvalidOperationException("Backend standard input is not redirected for this transport.");

                return _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            }

            internal static ProcessStartInfo CreateDefaultStartInfo() {
                var backendDir = Path.Combine(AppContext.BaseDirectory, "backend");
                var arch       = Config.CurrentArch;
                var command    = string.IsNullOrWhiteSpace(arch.RunCommand) ? "Backend.exe" : arch.RunCommand;
                var visibleBackendConsole =
                    Config.Debug.Enabled
                    && Config.Debug.ShowBackendConsole
                    && Config.IpcTransport == IPCTransport.NamedPipe;

                // If the command exists inside backend/ use that path; otherwise fall back to PATH lookup.
                var localPath = Path.Combine(backendDir, command);
                var fileName  = File.Exists(localPath) ? localPath : command;

                var psi = new ProcessStartInfo {
                    FileName               = fileName,
                    WorkingDirectory       = backendDir,
                    RedirectStandardInput  = !visibleBackendConsole,
                    RedirectStandardOutput = !visibleBackendConsole,
                    RedirectStandardError  = !visibleBackendConsole,
                    UseShellExecute        = false,
                    CreateNoWindow         = !(Config.Debug.Enabled && Config.Debug.ShowBackendConsole)
                };
                foreach (var arg in arch.RunArgs)
                    psi.ArgumentList.Add(arg);

                return psi;
            }

        #endregion

        #region Private methods

            private void StdOutHandler(object sender, DataReceivedEventArgs e) {
                if (e.Data is null) return;

                var handler = OnStdOut;
                if (handler is not null) {
                    _ = SafeInvokeAsync(handler, e.Data);
                }
            }

            private void StdErrHandler(object sender, DataReceivedEventArgs e) {
                if (e.Data is null) return;

                WriteBackendDebugLog(e.Data);

                var handler = OnStdErr;
                if (handler is not null) {
                    _ = SafeInvokeAsync(handler, e.Data);
                }
                else {
                    Console.Error.WriteLine(e.Data);
                }
            }

            private static async Task SafeInvokeAsync(Func<string, Task> handler, string data) {
                try {
                    await handler(data).ConfigureAwait(false);
                }
                catch { }
            }

            private static void WriteBackendDebugLog(string data) {
                if (!Config.Debug.Enabled || !Config.Debug.ShowBackendConsole)
                    return;

                try {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "lambdaflow.backend.log");
                    lock (LogLock) {
                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] {data}{Environment.NewLine}");
                    }
                }
                catch { }
            }

        #endregion
    }
}
