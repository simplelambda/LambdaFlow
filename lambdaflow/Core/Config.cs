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
        internal string Title { get; set; } = "CSharpTest";
        internal int Width { get; set; } = 800;
        internal int Height { get; set; } = 600;

        internal int MinWidth { get; set; } = 800;
        internal int MinHeight { get; set; } = 600;
        internal int MaxWidth { get; set; } = 0;
        internal int MaxHeight { get; set; } = 0;
    }

    internal static class Config
    {
        internal static readonly Platform Platform = GetPlatform();

        internal const string AppName = "CSharpTest";
        internal const string AppVersion = "1.0.0";
        internal const string OrgName = "LambdaFlow";
        internal static readonly WindowConfig Window = new WindowConfig();
        internal const string FrontendInitialHTML = "index.html";
        internal const string AppIcon = "app.ico";

        internal const bool DebugMode = false;

        internal static readonly SecurityMode SecurityMode = SecurityMode.HARDENED;

        internal const string Integrity = @"
			{
				""backend.pak"": ""64c0c132f444cd24613c8dc1f78814fb4ef848f4c2927b7aab0a489daae241461d0aa5c4cc6f52360305c32526feb6e8bf08197463c2be0a41a0743a5f8adc96"",
				""frontend.pak"": ""d9d65d38f86d5c5701fe6a3710262993a1b06a3fffc5a83f415d48b9c2799e3028521c6e330a33b27739eead45785981a9acff1a5a462245795cab200cad243a""
			}
		";
        internal const string PublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEzYKAFB1E2gj73Z0yOaFIKeZ5FQq5
gotF4Q7vle0zliv+zDWBT6l5LTw2hKOL968RYWQt50AwgCIfhgtjXyGKOA==
-----END PUBLIC KEY-----";



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