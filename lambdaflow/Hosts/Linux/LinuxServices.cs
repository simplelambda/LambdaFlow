using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Linux;

internal sealed class LinuxServices : IServices, IDisposable
{
    internal LinuxServices() {
        IPCBridge = new LinuxIPCBridge();
        WebView   = new LinuxWebView();
    }

    public IIPCBridge IPCBridge { get; }
    public IWebView WebView { get; }

    public void Dispose() {
        IPCBridge.Dispose();
        (WebView as IDisposable)?.Dispose();
    }
}
