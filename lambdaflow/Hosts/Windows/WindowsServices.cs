using System.Runtime.Versioning;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Windows {
    [SupportedOSPlatform("windows")]
    internal class WindowsServices : IServices {
        public IIPCBridge IPCBridge { get; }
        public IWebView WebView { get; }

        internal WindowsServices() {
            IPCBridge = new WindowsIPCBridge();
            WebView = new WindowsWebView();
        }
    }
}