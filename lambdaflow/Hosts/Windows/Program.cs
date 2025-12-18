using System;

using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Factories;
using lambdaflow.lambdaflow.Core.Services.Interfaces;

namespace lambdaflow.lambdaflow.Hosts.Windows
{
    internal static class Program
    {

        private static readonly IServices services = ServicesFactory.GetServices();

        [STAThread]
        static void Main(string[] args){
            // Bind IPC bridge event, when the backend sends a message to the frontend
            services.IPCBridge.Initialize();
            services.IPCBridge.OnProcessStdOut += async message =>
            {
                services.WebView.SendMessageToFrontend(message);
            };
            Console.WriteLine("IPC iniciado");

            // Initialize Webview
            services.WebView.Initialize(services.IPCBridge);
            Console.WriteLine("Webview Iniciado");

            // Start Application
            services.WebView.Navigate(Config.FrontendInitialHTML);
            services.WebView.Start();
        }
    }
}