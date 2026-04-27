using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace lambdaflow.lambdaflow.Core
{
    internal class WindowConfig
    {
        public string Title { get; set; } = "LambdaFlow app";
        public int Width { get; set; } = 800;
        public int Height { get; set; } = 600;

        public int MinWidth { get; set; } = 800;
        public int MinHeight { get; set; } = 600;
        public int MaxWidth { get; set; } = 0;
        public int MaxHeight { get; set; } = 0;
    }

    internal sealed class ArchConfig
    {
        [JsonPropertyName("runCommand")]
        public string RunCommand { get; set; } = "Backend.exe";

        [JsonPropertyName("runArgs")]
        public List<string> RunArgs { get; set; } = new List<string>();
    }

    internal sealed class PlatformConfig
    {
        [JsonPropertyName("archs")]
        public Dictionary<string, ArchConfig> Archs { get; set; } = new Dictionary<string, ArchConfig>();
    }

    internal sealed class AppConfig
    {
        [JsonPropertyName("appName")]
        public string AppName { get; set; } = "LambdaFlowApp";

        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; } = "1.0.0";

        [JsonPropertyName("organizationName")]
        public string OrganizationName { get; set; } = "LambdaFlow";

        [JsonPropertyName("appIcon")]
        public string AppIcon { get; set; } = "app.ico";

        [JsonPropertyName("frontendInitialHTML")]
        public string FrontendInitialHTML { get; set; } = "index.html";

        [JsonPropertyName("securityMode")]
        public string SecurityMode { get; set; } = "Hardened";

        [JsonPropertyName("ipcTransport")]
        public string IpcTransport { get; set; } = "NamedPipe";

        [JsonPropertyName("window")]
        public WindowConfig Window { get; set; } = new WindowConfig();

        [JsonPropertyName("platforms")]
        public Dictionary<string, PlatformConfig> Platforms { get; set; } = new Dictionary<string, PlatformConfig>();
    }

    internal static class Config
    {
        private static readonly AppConfig App = LoadAppConfig();

        internal static readonly Platform Platform = GetPlatform();

        internal static string AppName => App.AppName;
        internal static string AppVersion => App.AppVersion;
        internal static string OrgName => App.OrganizationName;
        internal static WindowConfig Window => App.Window ?? new WindowConfig();
        internal static string FrontendInitialHTML => App.FrontendInitialHTML;
        internal static string AppIcon => App.AppIcon;
        internal static ArchConfig CurrentArch => GetCurrentArch();

        internal const bool DebugMode = false;
        internal const string IntegrityManifestFile = "lambdaflow.integrity.json";

        internal static readonly SecurityMode SecurityMode = ParseSecurityMode(App.SecurityMode);
        internal static readonly IPCTransport IpcTransport = ParseIpcTransport(App.IpcTransport);

        private static AppConfig LoadAppConfig() {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
                return new AppConfig();

            var options = new JsonSerializerOptions {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), options) ?? new AppConfig();
        }

        private static SecurityMode ParseSecurityMode(string value) {
            return string.Equals(value, "Hardened", StringComparison.OrdinalIgnoreCase)
                ? SecurityMode.Hardened
                : throw new InvalidOperationException($"Unsupported security mode '{value}'.");
        }

        private static IPCTransport ParseIpcTransport(string value) {
            return value.ToLowerInvariant() switch {
                "namedpipe" => IPCTransport.NamedPipe,
                "stdio"     => IPCTransport.StdIO,
                _           => throw new InvalidOperationException($"Unsupported IPC transport '{value}'.")
            };
        }

        private static ArchConfig GetCurrentArch() {
            var platformKey = Platform switch {
                Platform.WINDOWS => "windows",
                Platform.LINUX   => "linux",
                Platform.MACOS   => "macos",
                _                => null
            };
            if (platformKey is null) return new ArchConfig();

            var archKey = RuntimeInformation.OSArchitecture switch {
                Architecture.X64   => "x64",
                Architecture.X86   => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm   => "arm",
                _                  => "x64"
            };

            if (App.Platforms.TryGetValue(platformKey, out var platform)
                && platform.Archs.TryGetValue(archKey, out var arch))
                return arch;

            return new ArchConfig();
        }

        private static Platform GetPlatform() {
            if (OperatingSystem.IsBrowser()) return Platform.WEB;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Platform.WINDOWS;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return Platform.LINUX;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Platform.MACOS;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID"))) return Platform.ANDROID;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS"))) return Platform.IOS;

            return Platform.UNKNOWN;
        }
    }
}
