using System;
using System.Threading;
using System.Threading.Tasks;

namespace lambdaflow.lambdaflow.Core.Services.Interfaces
{
    internal interface IIPCBridge : IDisposable
    {
        event Func<string, Task>? OnProcessStdOut;

        void Initialize();
        Task WaitUntilReadyAsync(CancellationToken ct = default);
        Task SendMessageToBackend(string message);
    }
}
