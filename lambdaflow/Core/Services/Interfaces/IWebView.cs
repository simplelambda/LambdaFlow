namespace lambdaflow.lambdaflow.Core.Services.Interfaces {
    internal interface IWebView {
        void Initialize(IIPCBridge ipcBridge);
        void Start();

        bool CheckAvailability();
        void InstallPrerequisites();

        void Navigate(string urlOrHtml);
        void SendMessageToFrontend(string message);

        void ModifyTitle(string title);
        void ModifySize(int width, int height);
        void ModfyMinSize(int width, int height);
        void ModifyMaxSize(int width, int height);
        void ModifyPosition(int x, int y);
        void Minimize();
        void Maximize();
    }
}