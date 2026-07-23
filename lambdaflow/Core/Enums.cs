[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("lambdaflow.windows")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("lambdaflow.linux")]

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
        Auto,
        StdIO,
        NamedPipe
    }
}
