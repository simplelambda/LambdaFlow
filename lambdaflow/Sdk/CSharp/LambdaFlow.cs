using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

// LambdaFlow C# SDK
// -----------------
// Drop-in single-file SDK for writing a LambdaFlow backend in C#.
//
// Quick start:
//
//     using LambdaFlow;
//
//     LambdaFlow.Receive<string, string>("uppercase", text => text.ToUpperInvariant());
//     LambdaFlow.Receive<Dog, string>   ("describeDog", dog => $"{dog.Name} is a {dog.Age}-year-old {dog.Breed}");
//
//     await LambdaFlow.RunAsync();
//
//     public record Dog(string Name, int Age, string Breed);
//
// Wire format (one JSON object per line):
//     { "kind": "<routing-key>", "id": "<uuid|null>", "payload": <any-json|null> }
//
// Transport is auto-detected: NamedPipe when LAMBDAFLOW_IPC_TRANSPORT=NamedPipe (with
// LAMBDAFLOW_PIPE_NAME), otherwise stdin/stdout.

public static class LambdaFlow
{
    private static readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> Handlers = new();
    private static readonly object                                                          WriteLock = new();
    private static readonly JsonSerializerOptions                                            JsonOpts  = new() {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private static TextWriter? _writer;

    // -- Public API ---------------------------------------------------------

    // Register a fire-and-forget handler. Any incoming message with this kind invokes the callback.
    public static void Receive<TPayload>(string kind, Action<TPayload> handler) {
        Handlers[kind] = payload => {
            handler(Deserialize<TPayload>(payload)!);
            return Task.FromResult<object?>(null);
        };
    }

    // Register a request handler. Whatever the callback returns is sent back as the response payload.
    public static void Receive<TRequest, TResponse>(string kind, Func<TRequest, TResponse> handler) {
        Handlers[kind] = payload => Task.FromResult<object?>(handler(Deserialize<TRequest>(payload)!));
    }

    // Async variant of the request handler.
    public static void Receive<TRequest, TResponse>(string kind, Func<TRequest, Task<TResponse>> handler) {
        Handlers[kind] = async payload => (object?)await handler(Deserialize<TRequest>(payload)!);
    }

    // Backward-compatible aliases.
    public static void On<TPayload>(string kind, Action<TPayload> handler) => Receive(kind, handler);
    public static void On<TRequest, TResponse>(string kind, Func<TRequest, TResponse> handler) => Receive(kind, handler);
    public static void On<TRequest, TResponse>(string kind, Func<TRequest, Task<TResponse>> handler) => Receive(kind, handler);

    // Send an event to the frontend (no response expected).
    public static void Send<T>(string kind, T payload) {
        WriteEnvelope(new Envelope { Kind = kind, Payload = SerializePayload(payload) });
    }

    // Send a bare event with no payload.
    public static void Send(string kind) {
        WriteEnvelope(new Envelope { Kind = kind });
    }

    // Build an ontology-friendly typed payload that can round-trip across languages.
    public static OntologyEntity<T> Entity<T>(string type, T data, int version = 1) {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Entity type must be non-empty.", nameof(type));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Entity version must be >= 1.");

        return new OntologyEntity<T> {
            Type    = type,
            Version = version,
            Data    = data,
        };
    }

    // Send a typed ontology payload to the frontend.
    public static void SendEntity<T>(string kind, string type, T data, int version = 1) {
        Send(kind, Entity(type, data, version));
    }

    // Run the message loop (blocks until the transport closes).
    public static async Task RunAsync(CancellationToken cancellationToken = default) {
        var (reader, writer) = await OpenTransportAsync().ConfigureAwait(false);
        _writer = writer;

        try {
            string? line;
            while (!cancellationToken.IsCancellationRequested
                   && (line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null) {
                _ = ProcessLineAsync(line);
            }
        }
        finally {
            (reader as IDisposable)?.Dispose();
            (writer as IDisposable)?.Dispose();
        }
    }

    // Synchronous wrapper.
    public static void Run() => RunAsync().GetAwaiter().GetResult();

    // -- Internals ----------------------------------------------------------

    private static async Task ProcessLineAsync(string line) {
        Envelope? env;
        try { env = JsonSerializer.Deserialize<Envelope>(line, JsonOpts); }
        catch { return; }
        if (env?.Kind is null) return;
        if (!Handlers.TryGetValue(env.Kind, out var handler)) return;

        try {
            var result = await handler(env.Payload).ConfigureAwait(false);

            // If the inbound message carried an id, this is a request — always reply (even with null payload)
            // so the frontend's await resolves. If no id, only forward when the handler returned a value.
            if (env.Id is not null)
                WriteEnvelope(new Envelope { Kind = env.Kind, Id = env.Id, Payload = SerializePayload(result) });
            else if (result is not null)
                WriteEnvelope(new Envelope { Kind = env.Kind, Payload = SerializePayload(result) });
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"[LambdaFlow] handler '{env.Kind}' threw: {ex}");
            if (env.Id is not null) {
                WriteEnvelope(new Envelope {
                    Kind    = env.Kind,
                    Id      = env.Id,
                    Payload = SerializePayload(new { error = ex.Message })
                });
            }
        }
    }

    private static T? Deserialize<T>(JsonElement? payload) {
        var effectivePayload = UnwrapOntologyPayload(payload);
        if (!effectivePayload.HasValue || effectivePayload.Value.ValueKind == JsonValueKind.Null || effectivePayload.Value.ValueKind == JsonValueKind.Undefined)
            return default;
        return effectivePayload.Value.Deserialize<T>(JsonOpts);
    }

    // Ontology payload convention (optional): { "$type": "...", "$v": 1, "data": {...} }
    // If present, handlers deserialize from `data` directly.
    private static JsonElement? UnwrapOntologyPayload(JsonElement? payload) {
        if (!payload.HasValue || payload.Value.ValueKind != JsonValueKind.Object)
            return payload;

        var value = payload.Value;
        if (!value.TryGetProperty("$type", out _))
            return payload;
        if (!value.TryGetProperty("data", out var data))
            return payload;

        return data;
    }

    private static JsonElement? SerializePayload(object? payload) {
        if (payload is null) return null;
        return JsonSerializer.SerializeToElement(payload, JsonOpts);
    }

    private static void WriteEnvelope(Envelope env) {
        if (_writer is null) return;
        var json = JsonSerializer.Serialize(env, JsonOpts);
        lock (WriteLock) {
            _writer.WriteLine(json);
            _writer.Flush();
        }
    }

    private static async Task<(TextReader reader, TextWriter writer)> OpenTransportAsync() {
        var transport = Environment.GetEnvironmentVariable("LAMBDAFLOW_IPC_TRANSPORT");
        if (string.Equals(transport, "NamedPipe", StringComparison.OrdinalIgnoreCase)) {
            var pipeName = Environment.GetEnvironmentVariable("LAMBDAFLOW_PIPE_NAME")
                ?? throw new InvalidOperationException("LAMBDAFLOW_PIPE_NAME is required for named pipe transport.");

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10_000).ConfigureAwait(false);

            var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: false) { AutoFlush = true };
            return (reader, writer);
        }
        return (Console.In, Console.Out);
    }

    public sealed class OntologyEntity<T> {
        [JsonPropertyName("$type")] public string Type    { get; init; } = string.Empty;
        [JsonPropertyName("$v")]    public int    Version { get; init; } = 1;
        [JsonPropertyName("data")]  public T?     Data    { get; init; }
    }

    private sealed class Envelope {
        [JsonPropertyName("kind")]    public string?       Kind    { get; set; }
        [JsonPropertyName("id")]      public string?       Id      { get; set; }
        [JsonPropertyName("payload")] public JsonElement?  Payload { get; set; }
    }
}
