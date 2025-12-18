using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Runtime.Versioning;
using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Core.Services.PlatformServices.WindowsServices{
    [SupportedOSPlatform("windows")]
    internal class WindowsIPCBridge : IIPCBridge{
        #region Variables

            #pragma warning disable CS8618 
                private BackendProcess _backend;
            #pragma warning restore CS8618

            private readonly CancellationTokenSource _cts = new CancellationTokenSource();
            private readonly Channel<string> _sendQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions{ SingleReader = true, SingleWriter = false });

            private Task? _sendLoopTask;
            private bool _initialized;

        #endregion

        #region Events

            public event Func<string, Task>? OnProcessStdOut;

        #endregion

        #region Public methods

            public void Initialize() {
                if (_initialized)
                    throw new InvalidOperationException("WindowsIPCBridge already initialized.");

                _backend = new BackendProcess();
                _backend.OnStdOut += HandleBackendStdOut;

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
                }
                catch { }

                try {
                    _sendLoopTask?.Wait(2000);
                }
                catch { }

                _backend?.Dispose();
                _cts.Dispose();
            }

        #endregion

        #region Private methods

            private async Task SendLoopAsync() {
                try {
                    await foreach (var msg in _sendQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                        await _backend.WriteLineAsync(msg, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch { }
            }

            private async Task HandleBackendStdOut(string message) {
                if (string.IsNullOrEmpty(message)) return;

                var handler = OnProcessStdOut;
                if (handler is null) return;

                try {
                    await handler(message).ConfigureAwait(false);
                }
                catch { }
            }

        #endregion
    }
}