using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.Versioning;
using lambdaflow.lambdaflow.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Windows{
    [SupportedOSPlatform("windows")]
    internal class WindowsWebView : IWebView{
        #region Variables

            private const string AppHost = "app.lambdaflow.localhost";
            private const string AppOrigin = "https://" + AppHost;
            private const string FrontendContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'self'; frame-ancestors 'none'";
            private static readonly object FrontendLogLock = new object();

            private WebView2? _view;
            private Form?     _host;
            private ZipArchive? _pak;

            private bool _initialized;

        #endregion

        #region Public methods

            public void Initialize(IIPCBridge ipcBridge){
                if (_initialized)
                    throw new InvalidOperationException("WindowsWebView ya ha sido inicializado.");

                CreateForm(ipcBridge);
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

                using (var client = new System.Net.Http.HttpClient())
                {
                    var bytes = client.GetByteArrayAsync(installerUrl).GetAwaiter().GetResult();
                    File.WriteAllBytes(tmp, bytes);
                }

                var psi = new ProcessStartInfo(tmp, "/silent /install") {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi)!.WaitForExit();
            }

            public void Navigate(string url) {
                if (_view is null || _view.CoreWebView2 is null)
                    return;

                // embedded HTML
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
                _view.CoreWebView2.Navigate($"{AppOrigin}/{safePath}");
            }

            public void SendMessageToFrontend(string message) {
                if (_view?.CoreWebView2 is null)
                    return;

                if (_host is not null && !_host.IsDisposed && _host.InvokeRequired) {
                    _host.BeginInvoke(new Action(() => SendMessageToFrontend(message)));
                    return;
                }

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

            // Creates the form and schedules WebView2 async init on Form.Load,
            // so it runs with the WinForms message pump already active (avoids STA deadlock).
            private void CreateForm(IIPCBridge ipcBridge) {
                _host = new Form {
                    Text          = Config.Window.Title ?? "LambdaFlow app",
                    WindowState   = FormWindowState.Maximized,
                    StartPosition = FormStartPosition.CenterScreen,
                    Width         = Config.Window.Width,
                    Height        = Config.Window.Height,
                    MinimumSize   = new Size(Config.Window.MinWidth, Config.Window.MinHeight),
                    KeyPreview    = true
                };

                if (Config.Window.MaxWidth > 0 && Config.Window.MaxHeight > 0)
                    _host.MaximumSize = new Size(Config.Window.MaxWidth, Config.Window.MaxHeight);

                _host.FormClosed += (_, _) => {
                    _pak?.Dispose();
                    ipcBridge.Dispose();
                };

                _host.KeyDown += (_, e) => {
                    if (e.KeyCode == Keys.F12 && Config.DebugMode) {
                        _view?.CoreWebView2?.OpenDevToolsWindow();
                        e.Handled = true;
                    }
                };

                try {
                    var iconPath = Path.Combine(AppContext.BaseDirectory, Config.AppIcon);
                    if (File.Exists(iconPath))
                        _host.Icon = new Icon(iconPath);
                }
                catch { }

                _view = new WebView2 { Dock = DockStyle.Fill };
                _host.Controls.Add(_view);

                _host.Load += async (_, _) => {
                    try {
                        await InitializeWebViewAsync(ipcBridge);
                    }
                    catch (Exception ex) {
                        MessageBox.Show(
                            ex.ToString(),
                            "LambdaFlow — WebView Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        _host?.Close();
                    }
                };
            }

            private async Task InitializeWebViewAsync(IIPCBridge ipcBridge) {
                var browserArgs = DetermineFastResolverArgs();
                var options     = new CoreWebView2EnvironmentOptions(browserArgs);

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Config.AppName);

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder:          userDataFolder,
                    options:                 options);

                await _view!.EnsureCoreWebView2Async(env);

                var settings = _view.CoreWebView2.Settings;
                settings.AreBrowserAcceleratorKeysEnabled  = false;
                settings.AreDefaultContextMenusEnabled     = false;
                settings.AreDevToolsEnabled                = Config.DebugMode;
                settings.AreHostObjectsAllowed             = false;
                settings.AreDefaultScriptDialogsEnabled    = false;
                settings.IsStatusBarEnabled                = false;

                await BindFrontendMethods();

                _view.CoreWebView2.WebMessageReceived += async (_, e) => {
                    var msg = e.TryGetWebMessageAsString();
                    try {
                        await HandleFrontendMessageAsync(msg, ipcBridge);
                    }
                    catch (Exception ex) {
                        Console.Error.WriteLine($"Error sending message to backend: {ex}");
                    }
                };

                if (Utilities.FrontFS is not null)
                    _pak = new ZipArchive(Utilities.FrontFS, ZipArchiveMode.Read, leaveOpen: true);
                else {
                    var frontendPakPath = Path.Combine(AppContext.BaseDirectory, "frontend.pak");
                    _pak = new ZipArchive(File.OpenRead(frontendPakPath), ZipArchiveMode.Read, leaveOpen: false);
                }

                _view.CoreWebView2.AddWebResourceRequestedFilter($"{AppOrigin}/*", CoreWebView2WebResourceContext.All);
                _view.CoreWebView2.WebResourceRequested += HandlePakRequest;

                await ipcBridge.WaitUntilReadyAsync();
                Navigate(Config.FrontendInitialHTML ?? "index.html");

                if (Config.DebugMode && Config.Debug.OpenFrontendDevToolsOnStart)
                    _view.CoreWebView2.OpenDevToolsWindow();
            }

            private async Task BindFrontendMethods() {
                await _view!.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        window.send = function(msg) {
                            window.chrome.webview.postMessage(msg);
                        };

                        window.receive = function(msg) {
                            console.warn('LambdaFlow: receive(msg) not implemented.');
                        };
                    ");

                if (Config.Debug.Enabled && Config.Debug.CaptureFrontendConsole) {
                    await _view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        (function () {
                            if (window.__lambdaFlowConsoleCaptureInstalled) return;
                            window.__lambdaFlowConsoleCaptureInstalled = true;

                            function serialize(value) {
                                if (value instanceof Error) return value.stack || value.message;
                                if (typeof value === 'string') return value;
                                try { return JSON.stringify(value); }
                                catch (_) { return String(value); }
                            }

                            function sendLog(level, args) {
                                try {
                                    window.chrome.webview.postMessage(JSON.stringify({
                                        kind: '__console',
                                        payload: {
                                            level: level,
                                            message: Array.prototype.slice.call(args).map(serialize).join(' '),
                                            timestamp: new Date().toISOString(),
                                            source: 'frontend'
                                        }
                                    }));
                                }
                                catch (_) { }
                            }

                            ['log', 'warn', 'error', 'info', 'debug'].forEach(function (level) {
                                var original = console[level] ? console[level].bind(console) : console.log.bind(console);
                                console[level] = function () {
                                    original.apply(console, arguments);
                                    sendLog(level, arguments);
                                };
                            });

                            window.addEventListener('error', function (event) {
                                sendLog('error', [
                                    event.message + ' at ' + event.filename + ':' + event.lineno + ':' + event.colno
                                ]);
                            });

                            window.addEventListener('unhandledrejection', function (event) {
                                sendLog('error', ['Unhandled promise rejection:', event.reason]);
                            });
                        })();
                    ");
                }
            }

            private async Task HandleFrontendMessageAsync(string message, IIPCBridge ipcBridge) {
                if (TryHandleInternalFrontendMessage(message))
                    return;

                await ipcBridge.SendMessageToBackend(message);
            }

            private bool TryHandleInternalFrontendMessage(string message) {
                try {
                    using var document = JsonDocument.Parse(message);
                    var root = document.RootElement;

                    if (!root.TryGetProperty("kind", out var kind)
                        || kind.GetString() != "__console")
                        return false;

                    if (!Config.Debug.Enabled || !Config.Debug.CaptureFrontendConsole)
                        return true;

                    var payload = root.TryGetProperty("payload", out var value)
                        ? value
                        : default;

                    var level = payload.ValueKind == JsonValueKind.Object
                        && payload.TryGetProperty("level", out var levelValue)
                            ? levelValue.GetString() ?? "log"
                            : "log";

                    var text = payload.ValueKind == JsonValueKind.Object
                        && payload.TryGetProperty("message", out var messageValue)
                            ? messageValue.GetString() ?? ""
                            : "";

                    var timestamp = payload.ValueKind == JsonValueKind.Object
                        && payload.TryGetProperty("timestamp", out var timestampValue)
                            ? timestampValue.GetString() ?? DateTime.Now.ToString("O")
                            : DateTime.Now.ToString("O");

                    var line = $"[{timestamp}] frontend {level}: {text}";
                    if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase))
                        Console.Error.WriteLine(line);
                    else
                        Console.WriteLine(line);

                    WriteFrontendDebugLog(line);
                    return true;
                }
                catch (JsonException) {
                    return false;
                }
            }

            private static void WriteFrontendDebugLog(string line) {
                try {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "lambdaflow.frontend.log");
                    lock (FrontendLogLock) {
                        File.AppendAllText(logPath, line + Environment.NewLine);
                    }
                }
                catch { }
            }

            private string? DetermineFastResolverArgs() {
                return $"--host-resolver-rules=\"MAP {AppHost} 0.0.0.0\"";
            }

            private void HandlePakRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e) {
                if (_pak is null || _view?.CoreWebView2 is null)
                    return;

                var relPath = GetPakRelativePath(e.Request.Uri);
                if (relPath is null) {
                    SetTextResponse(e, 400, "Bad Request", "Invalid frontend path.");
                    return;
                }

                byte[]? bytes = Utilities.ReadPAK(_pak, relPath);
                if (bytes is null) {
                    SetTextResponse(e, 404, "Not Found", "Frontend resource not found.");
                    return;
                }

                string contentType = Utilities.GetMimeType(relPath);

                var stream = new MemoryStream(bytes);
                e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", BuildFrontendHeaders(contentType));
            }

            private static string? GetPakRelativePath(string requestUri) {
                var uri  = new Uri(requestUri);
                var path = uri.AbsolutePath;

                if (path == "/" || string.IsNullOrEmpty(path))
                    path = "/index.html";
                else if (path.EndsWith("/", StringComparison.Ordinal))
                    path += "index.html";

                var relPath = Uri.UnescapeDataString(path.TrimStart('/')).Replace('\\', '/');
                foreach (var segment in relPath.Split('/')) {
                    if (segment == "..")
                        return null;
                }

                return relPath;
            }

            private void SetTextResponse(CoreWebView2WebResourceRequestedEventArgs e, int statusCode, string reasonPhrase, string body) {
                if (_view?.CoreWebView2 is null)
                    return;

                var bytes  = Encoding.UTF8.GetBytes(body);
                var stream = new MemoryStream(bytes);
                e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream,
                    statusCode,
                    reasonPhrase,
                    BuildFrontendHeaders("text/plain; charset=utf-8"));
            }

            private static string BuildFrontendHeaders(string contentType) {
                return $"Content-Type: {contentType}\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: {FrontendContentSecurityPolicy}";
            }

        #endregion
    }
}
