using lambdaflow.lambdaflow.Core;
using lambdaflow.lambdaflow.Core.Services.Interfaces;
using System;
using System.Collections.Generic;

namespace lambdaflow.lambdaflow.Core.Services.Factories{
    internal static class ServicesFactory{
        private static readonly Dictionary<Platform, Func<IServices>> Factories = new Dictionary<Platform, Func<IServices>>();

        internal static void Register(Platform platform, Func<IServices> factory) {
            Factories[platform] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal static IServices GetServices(){
            if (Factories.TryGetValue(Config.Platform, out var factory))
                return factory();

            throw new PlatformNotSupportedException($"'{Config.Platform}' is not supported.");
        }
    }
}
