using System.Threading.Channels;
using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Linux;

internal sealed class LinuxIPCBridge : IIPCBridge
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<string> _sendQueue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private BackendProcess? _backend;
    private Task? _sendLoop;
    private bool _initialized;
    private int _disposeState;

    public event Func<string, Task>? OnProcessStdOut;

    public void Initialize() {
        if (_initialized)
            throw new InvalidOperationException("LinuxIPCBridge is already initialized.");
        if (Config.IpcTransport != IPCTransport.StdIO)
            throw new PlatformNotSupportedException(
                "Linux currently uses the portable StdIO transport. Set ipcTransport to Auto or StdIO.");

        _backend = new BackendProcess();
        _backend.OnStdOut += ForwardBackendMessageAsync;
        _sendLoop = Task.Run(SendLoopAsync);
        _initialized = true;
    }

    public Task WaitUntilReadyAsync(CancellationToken ct = default) {
        if (!_initialized)
            throw new InvalidOperationException("LinuxIPCBridge is not initialized.");
        return Task.CompletedTask;
    }

    public async Task SendMessageToBackend(string message) {
        if (!_initialized || _backend is null)
            throw new InvalidOperationException("LinuxIPCBridge is not initialized.");
        if (_backend.HasExited)
            throw new InvalidOperationException("Backend already exited.");
        ArgumentNullException.ThrowIfNull(message);

        await _sendQueue.Writer.WriteAsync(message, _cts.Token).ConfigureAwait(false);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _sendQueue.Writer.TryComplete();
        _cts.Cancel();
        try { _sendLoop?.Wait(2000); } catch { }
        _backend?.Dispose();
        _cts.Dispose();
    }

    private async Task SendLoopAsync() {
        try {
            await foreach (var message in _sendQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false)) {
                if (_backend is not null)
                    await _backend.WriteLineAsync(message, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ForwardBackendMessageAsync(string message) {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var handler = OnProcessStdOut;
        if (handler is not null)
            await handler(message).ConfigureAwait(false);
    }
}
