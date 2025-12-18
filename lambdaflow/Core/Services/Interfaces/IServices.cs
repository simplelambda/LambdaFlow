namespace lambdaflow.lambdaflow.Core.Services.Interfaces {
    internal interface IServices {
        IIPCBridge IPCBridge { get; }
        IWebView WebView { get; }
    }
}