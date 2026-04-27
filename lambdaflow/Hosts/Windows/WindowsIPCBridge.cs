using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Runtime.Versioning;
using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Windows{
    [SupportedOSPlatform("windows")]
    internal class WindowsIPCBridge : IIPCBridge{
        #region Variables

            #pragma warning disable CS8618 
                private BackendProcess _backend;
            #pragma warning restore CS8618

            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly Channel<string> _sendQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions{ SingleReader = true, SingleWriter = false });
            private readonly TaskCompletionSource<StreamWriter> _pipeWriterSource = new TaskCompletionSource<StreamWriter>(TaskCreationOptions.RunContinuationsAsynchronously);

            private NamedPipeServerStream? _messagePipe;
            private StreamReader? _pipeReader;
            private StreamWriter? _pipeWriter;
            private Task? _sendLoopTask;
            private Task? _receiveLoopTask;
            private bool _initialized;

        #endregion

        #region Events

            public event Func<string, Task>? OnProcessStdOut;

        #endregion

        #region Public methods

            public void Initialize() {
                if (_initialized)
                    throw new InvalidOperationException("WindowsIPCBridge already initialized.");

                if (Config.IpcTransport == IPCTransport.NamedPipe)
                    InitializeNamedPipe();
                else
                    InitializeStdIO();

                _sendLoopTask = Task.Run(SendLoopAsync, _cts.Token);

                _initialized = true;
            }

            public async Task SendMessageToBackend(string message){
                if (!_initialized)      throw new InvalidOperationException("IPCBridge not inicialized.");
                if (_backend.HasExited) throw new InvalidOperationException("Backend already exited.");
                if (message is null)    throw new ArgumentNullException(nameof(message));

                await _sendQueue.Writer.WriteAsync(message, _cts.Token).ConfigureAwait(false);
            }

            public void Dispose(){
                _cts.Cancel();

                try {
                    _sendQueue.Writer.TryComplete();
                    _pipeWriterSource.TrySetCanceled(_cts.Token);
                }
                catch { }

                try {
                    _sendLoopTask?.Wait(2000);
                }
                catch { }

                try {
                    _receiveLoopTask?.Wait(2000);
                }
                catch { }

                try {
                    _pipeWriter?.Dispose();
                    _pipeReader?.Dispose();
                    _messagePipe?.Dispose();
                }
                catch { }

                _backend?.Dispose();
                _cts.Dispose();
            }

        #endregion

        #region Private methods

            private void InitializeStdIO() {
                _backend = new BackendProcess();
                _backend.OnStdOut += HandleBackendMessage;
            }

            private void InitializeNamedPipe() {
                var pipeName = $"lambdaflow-{Guid.NewGuid():N}";

                // PipeOptions.CurrentUserOnly restricts the pipe to the current user's SID.
                // This prevents other processes on the machine from connecting to the pipe
                // during the brief window between creation and backend attach.
                _messagePipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                var startInfo = BackendProcess.CreateDefaultStartInfo();
                startInfo.RedirectStandardInput = false;
                startInfo.Environment["LAMBDAFLOW_IPC_TRANSPORT"] = "NamedPipe";
                startInfo.Environment["LAMBDAFLOW_PIPE_NAME"] = pipeName;

                _backend = new BackendProcess(startInfo);
                _backend.OnStdOut += LogBackendStdOut;

                _receiveLoopTask = Task.Run(ReceiveLoopAsync, _cts.Token);
            }

            private async Task SendLoopAsync() {
                try {
                    StreamWriter? pipeWriter = null;
                    if (Config.IpcTransport == IPCTransport.NamedPipe)
                        pipeWriter = await _pipeWriterSource.Task.WaitAsync(_cts.Token).ConfigureAwait(false);

                    await foreach (var msg in _sendQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                        if (pipeWriter is not null)
                            await pipeWriter.WriteLineAsync(msg).ConfigureAwait(false);
                        else
                            await _backend.WriteLineAsync(msg, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }

            private async Task ReceiveLoopAsync() {
                if (_messagePipe is null)
                    return;

                try {
                    await _messagePipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);

                    _pipeReader = new StreamReader(_messagePipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                    _pipeWriter = new StreamWriter(_messagePipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                    _pipeWriterSource.TrySetResult(_pipeWriter);

                    while (!_cts.IsCancellationRequested) {
                        var message = await _pipeReader.ReadLineAsync().ConfigureAwait(false);
                        if (message is null)
                            break;

                        await HandleBackendMessage(message).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) {
                    _pipeWriterSource.TrySetException(ex);
                    Console.Error.WriteLine($"Named pipe IPC failed: {ex.Message}");
                }
            }

            private async Task HandleBackendMessage(string message) {
                if (string.IsNullOrEmpty(message)) return;

                var handler = OnProcessStdOut;
                if (handler is null) return;

                try {
                    await handler(message).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"Error forwarding backend message to frontend: {ex}");
                }
            }

            private static Task LogBackendStdOut(string message) {
                if (!string.IsNullOrEmpty(message))
                    Console.WriteLine($"Backend stdout: {message}");

                return Task.CompletedTask;
            }

        #endregion
    }
}
