using System;
using System.IO;
using System.Drawing;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using lambdaflow.lambdaflow.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

using static System.Net.Mime.MediaTypeNames;

namespace lambdaflow.lambdaflow.Hosts.Windows{
    [SupportedOSPlatform("windows")]
    internal class WindowsWebView : IWebView{
        #region Variables

            private WebView2? _view;
            private Form? _host;
            private ZipArchive? _pak;

            private bool _initialized;

        #endregion

        #region Public methods

            public async void Initialize(IIPCBridge ipcBridge){
                if (_initialized)
                    throw new InvalidOperationException("WindowsWebView ya ha sido inicializado.");

                InitializeAsync(ipcBridge).GetAwaiter().GetResult();
                _initialized = true;
            }

            public void Start() {
                if (_host is null) throw new InvalidOperationException("Initialize debe llamarse antes de Start.");

                Application.EnableVisualStyles();
                Application.Run(_host);
            }

            public bool CheckAvailability() {
                try {
                    var ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    return !string.IsNullOrEmpty(ver);
                }
                catch {
                    return false;
                }
            }

            public void InstallPrerequisites() {
                const string installerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
                var tmp = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebView2Bootstrapper.exe");

                using (var wc = new System.Net.WebClient())
                    wc.DownloadFile(installerUrl, tmp);

                var psi = new ProcessStartInfo(tmp, "/silent /install") {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi)!.WaitForExit();
            }

            public void Navigate(string url) {
                if (_view is null || _view.CoreWebView2 is null)
                    return;

                // embebed HTML
                if (url.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
                    _view.NavigateToString(url);
                    return;
                }

                // absolute URL
                if (Uri.TryCreate(url, UriKind.Absolute, out var abs)) {
                    _view.CoreWebView2.Navigate(abs.ToString());
                    return;
                }

                // relative route inside PAK
                var safePath = url.TrimStart('/');
                _view.CoreWebView2.Navigate($"https://app/{safePath}");
            }

            public void SendMessageToFrontend(string message) {
                if (_view?.CoreWebView2 is null)
                    return;

                var jsArg = JsonSerializer.Serialize(message);
                _ = _view.CoreWebView2.ExecuteScriptAsync($"window.receive({jsArg});");
            }

            public void ModifyTitle(string title) {
                if (_host is not null)
                    _host.Text = title;
            }

            public void ModifySize(int width, int height){
                if (_host is not null){
                    _host.Width = width;
                    _host.Height = height;
                }
            }

            public void ModfyMinSize(int width, int height){
                if (_host is not null)
                    _host.MinimumSize = new Size(width, height);       
            }

            public void ModifyMaxSize(int width, int height){
                if (_host is not null)
                    _host.MaximumSize = new Size(width, height);
            }

            public void ModifyPosition(int x, int y){
                if (_host is not null){
                    _host.StartPosition = FormStartPosition.Manual;
                    _host.Location = new Point(x, y);
                }
            }

            public void Minimize(){
                if (_host is not null)
                    _host.WindowState = FormWindowState.Minimized;
            }

            public void Maximize(){
                if (_host is not null)
                    _host.WindowState = FormWindowState.Maximized;
            }

        #endregion

        #region Private methods

            private async Task InitializeAsync(IIPCBridge ipcBridge) {
                _host = new Form {
                    Text = Config.Window.Title ?? "LambdaFlow app",
                    WindowState = FormWindowState.Maximized,
                    StartPosition = FormStartPosition.CenterScreen,
                    Width = Config.Window.Width,
                    Height = Config.Window.Height,
                    MinimumSize = new Size(Config.Window.MinWidth, Config.Window.MinHeight),
                    MaximumSize = new Size(Config.Window.MaxWidth, Config.Window.MaxHeight),
                };

                try {
                    if (File.Exists("app.ico"))
                        _host.Icon = new Icon("app.ico");
                }
                catch { }

                _view = new WebView2 { Dock = DockStyle.Fill };
                _host.Controls.Add(_view);

                var browserArgs = DetermineFastResolverArgs();
                var options = new CoreWebView2EnvironmentOptions(browserArgs);

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: null,
                    options: options);

                await _view.EnsureCoreWebView2Async(env);

                var settings = _view.CoreWebView2.Settings;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;

                await BindFrontendMethods();

                _view.CoreWebView2.WebMessageReceived += async (_, e) => {
                    var msg = e.TryGetWebMessageAsString();
                    Console.WriteLine($"Message from frontend: {msg}");

                    try {
                        await ipcBridge.SendMessageToBackend(msg);
                    }
                    catch (Exception ex) {
                        Console.Error.WriteLine($"Error sending message to backend: {ex}");
                    }
                };

                // Abrimos frontend.pak
                if (Utilities.FrontFS is not null)
                    _pak = new ZipArchive(Utilities.FrontFS, ZipArchiveMode.Read, leaveOpen: true);
                else
                    _pak = new ZipArchive(File.OpenRead("frontend.pak"), ZipArchiveMode.Read, leaveOpen: false);

                // Map https://app/* to .pak
                _view.CoreWebView2.AddWebResourceRequestedFilter("https://app/*", CoreWebView2WebResourceContext.All);
                _view.CoreWebView2.WebResourceRequested += HandlePakRequest;

                Navigate(Config.FrontendInitialHTML ?? "index.html");
            }

            private async Task BindFrontendMethods() {
                await _view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        window.send = function(msg) {
                            window.chrome.webview.postMessage(msg);
                        };

                        window.receive = function(msg) {
                            console.warn('LambdaFlow: receive(msg) not implemented.');
                        };
                    ");
            }

            private string? DetermineFastResolverArgs() {
                try {
                    var verStr = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    if (string.IsNullOrEmpty(verStr))
                        return null;

                    if (int.TryParse(verStr.Split('.')[0], out int major) && major >= 118) {
                        return @"--host-resolver-rules=""MAP app 0.0.0.0""";
                    }
                }
                catch {}

                TryAddHostsEntry();
                return null;
            }

            private static void TryAddHostsEntry() {
                try {
                    string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

                    var lines = File.ReadAllLines(hostsPath);
                    foreach (var ln in lines) {
                        if (ln.Contains(" app"))
                            return;
                    }

                    File.AppendAllText(hostsPath, $"{Environment.NewLine}127.0.0.1    app{Environment.NewLine}");
                }
                catch { }
            }

            private void HandlePakRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e) {
                if (_pak is null || _view?.CoreWebView2 is null)
                    return;

                var uri = new Uri(e.Request.Uri);
                var path = uri.AbsolutePath;

                if (path == "/" || string.IsNullOrEmpty(path))
                    path = "/index.html";
                else if (path.EndsWith("/"))
                    path += "index.html";

                var relPath = Uri.UnescapeDataString(path.TrimStart('/'));

                byte[]? bytes = Utilities.ReadPAK(_pak, relPath);
                if (bytes == null)
                    return;

                string contentType = Utilities.GetMimeType(relPath);

                Console.WriteLine($"PAK request for: {relPath} (Content-Type: {contentType})");

                var stream = new MemoryStream(bytes);
                e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream,
                    statusCode: 200,
                    reasonPhrase: "OK",
                    headers: $"Content-Type: {contentType}");
            }

        #endregion
    }
}