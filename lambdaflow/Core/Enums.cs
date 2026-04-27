[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("lambdaflow.windows")]

namespace lambdaflow.lambdaflow.Core
{

    internal enum Platform
    {
        WINDOWS,
        LINUX,
        MACOS,
        ANDROID,
        IOS,
        WEB,
        UNKNOWN
    }

    internal enum SecurityMode
    {
        Hardened
    }

    internal enum IPCTransport
    {
        StdIO,
        NamedPipe
    }
}
