using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LambdaFlow{
    internal class BackendProcess : IDisposable{
        #region Variables

            private readonly Process _process;

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
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
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
                return _process.StandardInput.WriteLineAsync(line.AsMemory(), ct);
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

            private static ProcessStartInfo CreateDefaultStartInfo() {
                var backendDir = Path.Combine(AppContext.BaseDirectory, "backend");
                var backendPath = Path.Combine(backendDir, "Backend.exe");

                if (!File.Exists(backendPath))
                    throw new FileNotFoundException($"Backend executable not found at '{backendPath}'.");

                return new ProcessStartInfo {
                    FileName = backendPath,
                    WorkingDirectory = backendDir,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

        #endregion
    }
}