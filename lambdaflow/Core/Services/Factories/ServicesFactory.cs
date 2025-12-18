using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;
using LambdaFlow;
using System;

namespace lambdaflow.lambdaflow.Core.Services.Factories{
    internal static class ServicesFactory{
        internal static IServices GetServices(){
            #pragma warning disable CA1416

            return Config.Platform switch {
                Platform.WINDOWS => new WindowsServices(),
                _ => throw new PlatformNotSupportedException($"'{Config.Platform}' is not supported.")
            };

            #pragma warning restore CA1416
        }
    }
}