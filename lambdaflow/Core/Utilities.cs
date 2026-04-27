using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace lambdaflow.lambdaflow.Core
{
    internal static class Utilities
    {
        #region Variables

            internal static FileStream? FrontFS;

        #endregion

        #region Internal methods

        internal static Stream? GetEmbeddedResourceStream(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resName = $"lambdaflow.lambdaflow.TMP.{resourceName}";

            return assembly.GetManifestResourceStream(resName) ?? throw new FileNotFoundException($"Resource '{resName}' not found.");
        }

        internal static string GetEmbeddedResourceString(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resName = $"lambdaflow.lambdaflow.TMP.{resourceName}";
            var stream = assembly.GetManifestResourceStream(resName) ?? throw new FileNotFoundException($"Resource '{resName}' not found.");

            if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd();
        }

        internal static string GetMimeType(string path)
        {
            var ext = Path.GetExtension(path);

            return ext switch
            {
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".otf" => "application/vnd.ms-fontobject",
                ".xml" => "application/xml",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".txt" => "text/plain",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".mp3" => "audio/mpeg",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".wasm" => "application/wasm",
                _ => "application/octet-stream"
            };
        }

        internal static byte[]? ReadPAK(ZipArchive pak, string relativePath)
        {
            try
            {
                var entry = pak.GetEntry(relativePath.Replace('\\', '/'));

                if (entry is null) return null;

                using Stream entryStream = entry.Open();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        #endregion


    }
}
