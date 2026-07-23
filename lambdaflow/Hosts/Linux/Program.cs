using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Factories;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Linux;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

        try {
            IntegrityVerifier.VerifyApplicationBundle();
            using var services = CreateServices();

            services.IPCBridge.Initialize();
            services.IPCBridge.OnProcessStdOut += message => {
                services.WebView.SendMessageToFrontend(message);
                return Task.CompletedTask;
            };

            services.WebView.Initialize(services.IPCBridge);
            services.WebView.Start();
            return 0;
        }
        catch (Exception ex) {
            WriteCrashLog(ex);
            Console.Error.WriteLine($"LambdaFlow fatal error:{Environment.NewLine}{ex}");
            return 1;
        }
    }

    private static LinuxServices CreateServices() {
        ServicesFactory.Register(Platform.LINUX, () => new LinuxServices());
        return (LinuxServices)ServicesFactory.GetServices();
    }

    private static void WriteCrashLog(Exception ex) {
        try {
            var logPath = Path.Combine(AppContext.BaseDirectory, "lambdaflow.crash.log");
            File.WriteAllText(logPath, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { }
    }
}
