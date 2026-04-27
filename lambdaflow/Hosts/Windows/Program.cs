using System;
using System.IO;
using System.Windows.Forms;

using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Factories;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Windows
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args){
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException        += (_, e) => ShowFatalError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ShowFatalError(e.ExceptionObject as Exception
                    ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error"));

            try {
                Run();
            }
            catch (Exception ex) {
                ShowFatalError(ex);
            }
        }

        private static void Run() {
            IntegrityVerifier.VerifyApplicationBundle();
            var services = CreateServices();

            services.IPCBridge.Initialize();
            services.IPCBridge.OnProcessStdOut += async message =>
            {
                services.WebView.SendMessageToFrontend(message);
            };

            services.WebView.Initialize(services.IPCBridge);
            services.WebView.Start();
        }

        private static void ShowFatalError(Exception ex) {
            try {
                var logPath = Path.Combine(AppContext.BaseDirectory, "lambdaflow.crash.log");
                File.WriteAllText(logPath, $"[{DateTime.Now:O}]\r\n{ex}\r\n");
            }
            catch { }

            MessageBox.Show(
                ex.ToString(),
                "LambdaFlow — Fatal Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.Exit(1);
        }

        private static IServices CreateServices() {
            ServicesFactory.Register(Platform.WINDOWS, () => new WindowsServices());
            return ServicesFactory.GetServices();
        }
    }
}
