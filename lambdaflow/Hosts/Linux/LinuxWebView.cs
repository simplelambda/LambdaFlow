using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;
using Photino.NET;

namespace lambdaflow.lambdaflow.Hosts.Linux;

internal sealed class LinuxWebView : IWebView, IDisposable
{
    private const string AppScheme = "lambdaflow";
    private const string AppOrigin = AppScheme + "://app/";
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'self'; frame-ancestors 'none'";

    private static readonly object FrontendLogLock = new();

    private PhotinoWindow? _window;
    private ZipArchive? _pak;
    private IIPCBridge? _ipcBridge;
    private readonly ConcurrentQueue<string> _pendingFrontendMessages = new();
    private bool _initialized;
    private bool _nativeReady;
    private bool _frontendReady;
    private bool _disposed;

    public void Initialize(IIPCBridge ipcBridge) {
        if (_initialized)
            throw new InvalidOperationException("LinuxWebView is already initialized.");

        _ipcBridge = ipcBridge ?? throw new ArgumentNullException(nameof(ipcBridge));
        OpenFrontendPak();
        CreateWindow();
        _initialized = true;
    }

    public void Start() {
        if (!_initialized || _window is null)
            throw new InvalidOperationException("Initialize must be called before Start.");
        if (!CheckAvailability())
            throw new PlatformNotSupportedException(
                "GTK 3 and WebKitGTK 4.1 are required. Install your distribution packages for gtk3 and webkit2gtk-4.1.");

        _ipcBridge!.WaitUntilReadyAsync().GetAwaiter().GetResult();
        _window.WaitForClose();
    }

    public bool CheckAvailability() {
        return CanLoad("libgtk-3.so.0") && CanLoad("libwebkit2gtk-4.1.so.0");
    }

    public void InstallPrerequisites() {
        throw new PlatformNotSupportedException(
            "Install GTK 3 and WebKitGTK 4.1 with your distribution package manager; LambdaFlow does not modify the system.");
    }

    public void Navigate(string urlOrHtml) {
        if (_window is null)
            return;

        if (urlOrHtml.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
            _window.LoadRawString(urlOrHtml);
            return;
        }

        if (Uri.TryCreate(urlOrHtml, UriKind.Absolute, out var absolute)) {
            _window.Load(absolute);
            return;
        }

        _window.Load(new Uri(AppOrigin + urlOrHtml.TrimStart('/')));
    }

    public void SendMessageToFrontend(string message) {
        if (_window is null || !_nativeReady || !_frontendReady) {
            _pendingFrontendMessages.Enqueue(message);
            return;
        }

        try {
            _window.SendWebMessage(message);
        }
        catch (ApplicationException) when (!_nativeReady || _disposed) {
            _pendingFrontendMessages.Enqueue(message);
        }
    }

    public void ModifyTitle(string title) => _window?.SetTitle(title);
    public void ModifySize(int width, int height) => _window?.SetSize(width, height);
    public void ModfyMinSize(int width, int height) => _window?.SetMinSize(width, height);
    public void ModifyMaxSize(int width, int height) => _window?.SetMaxSize(width, height);
    public void ModifyPosition(int x, int y) => _window?.SetLocation(new System.Drawing.Point(x, y));
    public void Minimize() => _window?.SetMinimized(true);
    public void Maximize() => _window?.SetMaximized(true);

    public void Dispose() {
        if (_disposed)
            return;

        _disposed = true;
        _pak?.Dispose();
        _pak = null;
    }

    private void OpenFrontendPak() {
        if (Utilities.FrontFS is not null) {
            _pak = new ZipArchive(Utilities.FrontFS, ZipArchiveMode.Read, leaveOpen: true);
            return;
        }

        var pakPath = Path.Combine(AppContext.BaseDirectory, "frontend.pak");
        _pak = new ZipArchive(File.OpenRead(pakPath), ZipArchiveMode.Read, leaveOpen: false);
    }

    private void CreateWindow() {
        var window = new PhotinoWindow()
            .SetTitle(Config.Window.Title ?? Config.AppName)
            .SetUseOsDefaultSize(false)
            .SetSize(Config.Window.Width, Config.Window.Height)
            .SetMinSize(Config.Window.MinWidth, Config.Window.MinHeight)
            .SetContextMenuEnabled(Config.DebugMode)
            .SetDevToolsEnabled(Config.DebugMode)
            .Center()
            .RegisterCustomSchemeHandler(AppScheme, HandleFrontendRequest)
            .RegisterWebMessageReceivedHandler((_, message) => _ = HandleFrontendMessageAsync(message))
            .RegisterWindowCreatedHandler((_, _) => _nativeReady = true)
            .RegisterWindowClosingHandler((_, _) => {
                _ipcBridge?.Dispose();
                Dispose();
                return false;
            });

        if (Config.Window.MaxWidth > 0 && Config.Window.MaxHeight > 0)
            window.SetMaxSize(Config.Window.MaxWidth, Config.Window.MaxHeight);

        var iconPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Config.AppIcon));
        if (File.Exists(iconPath))
            window.SetIconFile(iconPath);

        _window = window;
        Navigate(Config.FrontendInitialHTML ?? "index.html");
    }

    private void FlushPendingFrontendMessages() {
        while (_nativeReady
               && _frontendReady
               && _window is not null
               && _pendingFrontendMessages.TryDequeue(out var message)) {
            _window.SendWebMessage(message);
        }
    }

    private Stream HandleFrontendRequest(object sender, string scheme, string url, out string contentType) {
        var relativePath = GetPakRelativePath(url);
        if (relativePath is null) {
            contentType = "text/plain; charset=utf-8";
            return TextStream("Invalid frontend path.");
        }

        var bytes = _pak is null ? null : Utilities.ReadPAK(_pak, relativePath);
        if (bytes is null) {
            contentType = "text/html; charset=utf-8";
            var notFound = _pak is null ? null : Utilities.ReadPAK(_pak, "404.html");
            return new MemoryStream(notFound ?? Encoding.UTF8.GetBytes("<h1>404 — Frontend resource not found</h1>"));
        }

        contentType = Utilities.GetMimeType(relativePath);
        if (contentType == "text/html")
            bytes = AddHtmlBootstrap(bytes);

        return new MemoryStream(bytes, writable: false);
    }

    private async Task HandleFrontendMessageAsync(string message) {
        if (TryHandleInternalFrontendMessage(message))
            return;

        try {
            if (_ipcBridge is not null)
                await _ipcBridge.SendMessageToBackend(message).ConfigureAwait(false);
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"Error sending frontend message to backend: {ex.Message}");
        }
    }

    private static byte[] AddHtmlBootstrap(byte[] source) {
        var html = Encoding.UTF8.GetString(source);
        var consoleCapture = Config.Debug.Enabled && Config.Debug.CaptureFrontendConsole
            ? """
              <script>
              (function () {
                function serialize(value) {
                  if (value instanceof Error) return value.stack || value.message;
                  if (typeof value === 'string') return value;
                  try { return JSON.stringify(value); } catch (_) { return String(value); }
                }
                function forward(level, args) {
                  try {
                    window.send(JSON.stringify({
                      kind: '__console',
                      payload: {
                        level: level,
                        message: Array.prototype.slice.call(args).map(serialize).join(' '),
                        timestamp: new Date().toISOString(),
                        source: 'frontend'
                      }
                    }));
                  } catch (_) {}
                }
                ['log', 'warn', 'error', 'info', 'debug'].forEach(function (level) {
                  var original = console[level] ? console[level].bind(console) : console.log.bind(console);
                  console[level] = function () {
                    original.apply(console, arguments);
                    forward(level, arguments);
                  };
                });
                window.addEventListener('error', function (event) {
                  forward('error', [event.message + ' at ' + event.filename + ':' + event.lineno]);
                });
                window.addEventListener('unhandledrejection', function (event) {
                  forward('error', ['Unhandled promise rejection:', event.reason]);
                });
              })();
              </script>
              """
            : "";
        var bootstrap = $$"""
        <meta http-equiv="Content-Security-Policy" content="{{ContentSecurityPolicy}}">
        <script>
        (function () {
          window.__lambdaFlowInboundQueue = window.__lambdaFlowInboundQueue || [];
          window.send = function (message) { window.external.sendMessage(message); };
          window.receive = window.receive || function (message) {
            window.__lambdaFlowInboundQueue.push(message);
          };
          window.external.receiveMessage(function (message) { window.receive(message); });
          window.external.sendMessage(JSON.stringify({ kind: '__lambdaflow_ready' }));
        })();
        </script>
        {{consoleCapture}}
        """;

        var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (head >= 0) {
            var close = html.IndexOf('>', head);
            if (close >= 0)
                html = html.Insert(close + 1, bootstrap);
            else
                html = bootstrap + html;
        }
        else {
            html = bootstrap + html;
        }

        return Encoding.UTF8.GetBytes(html);
    }

    private static string? GetPakRelativePath(string requestUrl) {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath;
        if (path == "/" || string.IsNullOrEmpty(path))
            path = "/index.html";
        else if (path.EndsWith("/", StringComparison.Ordinal))
            path += "index.html";

        var relativePath = Uri.UnescapeDataString(path.TrimStart('/')).Replace('\\', '/');
        if (relativePath.Split('/').Any(segment => segment is ".." or "."))
            return null;

        return relativePath;
    }

    private bool TryHandleInternalFrontendMessage(string message) {
        try {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("kind", out var kind))
                return false;
            if (kind.GetString() == "__lambdaflow_ready") {
                _frontendReady = true;
                FlushPendingFrontendMessages();
                return true;
            }
            if (kind.GetString() != "__console")
                return false;
            if (!Config.Debug.Enabled || !Config.Debug.CaptureFrontendConsole)
                return true;

            var payload = root.TryGetProperty("payload", out var value) ? value : default;
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
                    ? timestampValue.GetString() ?? DateTimeOffset.Now.ToString("O")
                    : DateTimeOffset.Now.ToString("O");

            var line = $"[{timestamp}] frontend {level}: {text}";
            if (level is "error" or "warn")
                Console.Error.WriteLine(line);
            else
                Console.WriteLine(line);

            lock (FrontendLogLock) {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "lambdaflow.frontend.log"),
                    line + Environment.NewLine);
            }
            return true;
        }
        catch (JsonException) {
            return false;
        }
        catch {
            return true;
        }
    }

    private static bool CanLoad(string libraryName) {
        if (!NativeLibrary.TryLoad(libraryName, out var handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    private static MemoryStream TextStream(string text) {
        return new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
    }
}
