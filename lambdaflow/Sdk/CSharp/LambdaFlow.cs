using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Single-file LambdaFlow backend SDK.
///
/// Common API across backend SDKs:
/// configure, receive/on/handle, unhandle, send/emit, request,
/// respond/reject, entity helpers, run/stop and pendingCount.
/// </summary>
public static class LambdaFlow
{
    public const string Version = "1.3.0";

    private const string ResultSuffix = ".result";

    private static readonly ConcurrentDictionary<string, HandlerEntry> Handlers = new();
    private static readonly ConcurrentDictionary<string, PendingRequest> Pending = new();
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object StateLock = new();
    private static CancellationTokenSource? _runCts;
    private static TextReader? _reader;
    private static TextWriter? _writer;
    private static IDisposable? _transportOwner;
    private static int _defaultTimeoutMs = 30_000;
    private static bool _unwrapEntities = true;
    private static bool _warnOnUnhandled;
    private static bool _replyToEvents;

    public static void Configure(
        int? timeoutMs = null,
        bool? unwrapEntities = null,
        bool? warnOnUnhandled = null,
        bool? replyToEvents = null) {
        if (timeoutMs is not null) {
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "timeoutMs must be greater than zero.");
            _defaultTimeoutMs = timeoutMs.Value;
        }
        if (unwrapEntities is not null) _unwrapEntities = unwrapEntities.Value;
        if (warnOnUnhandled is not null) _warnOnUnhandled = warnOnUnhandled.Value;
        if (replyToEvents is not null) _replyToEvents = replyToEvents.Value;
    }

    public static void Receive<TPayload>(string kind, Action<TPayload> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        Register(kind, async (payload, _) => {
            handler(Deserialize<TPayload>(payload)!);
            await Task.CompletedTask;
            return null;
        });
    }

    public static void Receive<TRequest, TResponse>(string kind, Func<TRequest, TResponse> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        Register(kind, (payload, _) =>
            Task.FromResult<object?>(handler(Deserialize<TRequest>(payload)!)));
    }

    public static void Receive<TRequest, TResponse>(
        string kind,
        Func<TRequest, Meta, TResponse> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        Register(kind, (payload, meta) =>
            Task.FromResult<object?>(handler(Deserialize<TRequest>(payload)!, meta)));
    }

    public static void Receive<TRequest, TResponse>(
        string kind,
        Func<TRequest, Task<TResponse>> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        Register(kind, async (payload, _) =>
            await handler(Deserialize<TRequest>(payload)!).ConfigureAwait(false));
    }

    public static void Receive<TRequest, TResponse>(
        string kind,
        Func<TRequest, Meta, Task<TResponse>> handler) {
        ArgumentNullException.ThrowIfNull(handler);
        Register(kind, async (payload, meta) =>
            await handler(Deserialize<TRequest>(payload)!, meta).ConfigureAwait(false));
    }

    public static void On<TPayload>(string kind, Action<TPayload> handler) => Receive(kind, handler);
    public static void On<TRequest, TResponse>(string kind, Func<TRequest, TResponse> handler) => Receive(kind, handler);
    public static void On<TRequest, TResponse>(string kind, Func<TRequest, Task<TResponse>> handler) => Receive(kind, handler);
    public static void Handle<TRequest, TResponse>(string kind, Func<TRequest, TResponse> handler) => Receive(kind, handler);
    public static void Handle<TRequest, TResponse>(string kind, Func<TRequest, Task<TResponse>> handler) => Receive(kind, handler);

    public static bool Unhandle(string kind) {
        AssertKind(kind);
        return Handlers.TryRemove(kind, out _);
    }

    public static bool Off(string kind) => Unhandle(kind);

    public static void Send<T>(string kind, T payload) =>
        WriteEnvelopeAsync(new Envelope { Kind = kind, Payload = SerializePayload(payload) })
            .GetAwaiter().GetResult();

    public static void Send(string kind) => Send<object?>(kind, null);
    public static void Emit<T>(string kind, T payload) => Send(kind, payload);
    public static void Emit(string kind) => Send(kind);

    public static async Task<TResponse?> RequestAsync<TResponse>(
        string kind,
        object? payload = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default) {
        AssertKind(kind);
        var effectiveTimeout = timeoutMs ?? _defaultTimeoutMs;
        if (effectiveTimeout <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "timeoutMs must be greater than zero.");

        var id = Guid.NewGuid().ToString();
        var completion = new TaskCompletionSource<JsonElement?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingRequest(kind, completion);
        if (!Pending.TryAdd(id, pending))
            throw new InvalidOperationException("Could not register LambdaFlow request.");

        try {
            await WriteEnvelopeAsync(new Envelope {
                Kind = kind,
                Id = id,
                Payload = SerializePayload(payload)
            }, cancellationToken).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(effectiveTimeout));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            try {
                var response = await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                return Deserialize<TResponse>(response);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                throw new LambdaFlowException(
                    $"Request '{kind}' timed out.",
                    "REQUEST_TIMEOUT",
                    new { kind, id, timeoutMs = effectiveTimeout });
            }
        }
        finally {
            Pending.TryRemove(id, out _);
        }
    }

    public static TResponse? Request<TResponse>(
        string kind,
        object? payload = null,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync<TResponse>(kind, payload, timeoutMs, cancellationToken)
            .GetAwaiter().GetResult();

    public static void Respond(string kind, string id, object? payload = null) {
        AssertKind(kind);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("respond requires a request id.", nameof(id));
        WriteEnvelopeAsync(new Envelope {
            Kind = kind.EndsWith(ResultSuffix, StringComparison.Ordinal) ? kind : kind + ResultSuffix,
            Id = id,
            Ok = true,
            Payload = SerializePayload(payload)
        }).GetAwaiter().GetResult();
    }

    public static void Reject(string kind, string id, object error) {
        AssertKind(kind);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("reject requires a request id.", nameof(id));
        WriteEnvelopeAsync(new Envelope {
            Kind = kind.EndsWith(ResultSuffix, StringComparison.Ordinal) ? kind : kind + ResultSuffix,
            Id = id,
            Ok = false,
            Error = SerializePayload(ToErrorObject(error))
        }).GetAwaiter().GetResult();
    }

    public static OntologyEntity<T> Entity<T>(string type, T data, int version = 1) {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Entity type must be non-empty.", nameof(type));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Entity version must be at least 1.");
        return new OntologyEntity<T> { Type = type, Version = version, Data = data };
    }

    public static bool IsEntity(JsonElement? payload) {
        return payload is { ValueKind: JsonValueKind.Object }
            && payload.Value.TryGetProperty("$type", out var type)
            && type.ValueKind == JsonValueKind.String
            && payload.Value.TryGetProperty("data", out _);
    }

    public static JsonElement? UnwrapEntity(JsonElement? payload) {
        return IsEntity(payload) && payload!.Value.TryGetProperty("data", out var data)
            ? data.Clone()
            : payload;
    }

    public static void SendEntity<T>(string kind, string type, T data, int version = 1) =>
        Send(kind, Entity(type, data, version));

    public static Task<TResponse?> RequestEntityAsync<TResponse, TData>(
        string kind,
        string type,
        TData data,
        int version = 1,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync<TResponse>(
            kind,
            Entity(type, data, version),
            timeoutMs,
            cancellationToken);

    public static async Task RunAsync(CancellationToken cancellationToken = default) {
        var work = new List<Task>();
        CancellationTokenSource runCts;
        lock (StateLock) {
            if (_runCts is not null)
                throw new InvalidOperationException("LambdaFlow is already running.");
            runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCts = runCts;
        }

        try {
            (_reader, _writer, _transportOwner) = await OpenTransportAsync().ConfigureAwait(false);
            while (!runCts.IsCancellationRequested) {
                var line = await _reader.ReadLineAsync(runCts.Token).ConfigureAwait(false);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                work.RemoveAll(task => task.IsCompleted);
                work.Add(Task.Run(() => ProcessLineAsync(line), runCts.Token));
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested) { }
        finally {
            try {
                await Task.WhenAll(work).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            Stop();
        }
    }

    public static void Run() => RunAsync().GetAwaiter().GetResult();

    public static void Stop() {
        CancellationTokenSource? cts;
        lock (StateLock) {
            cts = _runCts;
            _runCts = null;
        }

        try { cts?.Cancel(); } catch { }
        try { _transportOwner?.Dispose(); } catch { }
        _reader = null;
        _writer = null;
        _transportOwner = null;
        cts?.Dispose();

        foreach (var item in Pending) {
            if (Pending.TryRemove(item.Key, out var pending))
                pending.Completion.TrySetException(
                    new LambdaFlowException("LambdaFlow stopped.", "SDK_STOPPED"));
        }
    }

    public static int PendingCount() => Pending.Count;

    private static void Register(
        string kind,
        Func<JsonElement?, Meta, Task<object?>> handler) {
        AssertKind(kind);
        Handlers[kind] = new HandlerEntry(handler);
    }

    private static async Task ProcessLineAsync(string line) {
        Envelope? envelope;
        try {
            envelope = JsonSerializer.Deserialize<Envelope>(line, JsonOptions);
        }
        catch (JsonException) {
            Console.Error.WriteLine("[LambdaFlow] ignored malformed JSON.");
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Kind))
            return;

        if (!string.IsNullOrWhiteSpace(envelope.Id)
            && Pending.TryRemove(envelope.Id, out var pending)) {
            if (envelope.Ok == false || envelope.Error is not null
                || HasLegacyError(envelope.Payload)) {
                pending.Completion.TrySetException(ErrorFromEnvelope(envelope));
            }
            else {
                pending.Completion.TrySetResult(
                    _unwrapEntities ? UnwrapEntity(envelope.Payload) : envelope.Payload);
            }
            return;
        }

        if (!Handlers.TryGetValue(envelope.Kind, out var entry)) {
            if (_warnOnUnhandled)
                Console.Error.WriteLine($"[LambdaFlow] no handler for '{envelope.Kind}'.");
            return;
        }

        var rawPayload = envelope.Payload;
        var delivered = _unwrapEntities ? UnwrapEntity(rawPayload) : rawPayload;
        var meta = new Meta(
            envelope.Kind,
            envelope.Id,
            envelope.Ok,
            rawPayload,
            IsEntity(rawPayload),
            EntityType(rawPayload),
            EntityVersion(rawPayload),
            DateTimeOffset.UtcNow);

        try {
            var result = await entry.Handler(delivered, meta).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(envelope.Id))
                Respond(envelope.Kind, envelope.Id, result);
            else if (_replyToEvents && result is not null)
                Send(envelope.Kind, result);
        }
        catch (Exception ex) {
            Console.Error.WriteLine($"[LambdaFlow] handler '{envelope.Kind}' failed: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(envelope.Id))
                Reject(envelope.Kind, envelope.Id, ex);
        }
    }

    private static async Task WriteEnvelopeAsync(
        Envelope envelope,
        CancellationToken cancellationToken = default) {
        AssertKind(envelope.Kind);
        var writer = _writer
            ?? throw new InvalidOperationException(
                "LambdaFlow transport is not running. Call Run/RunAsync before sending.");
        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            WriteLock.Release();
        }
    }

    private static async Task<(TextReader Reader, TextWriter Writer, IDisposable? Owner)> OpenTransportAsync() {
        if (string.Equals(
                Environment.GetEnvironmentVariable("LAMBDAFLOW_IPC_TRANSPORT"),
                "NamedPipe",
                StringComparison.OrdinalIgnoreCase)) {
            var pipeName = Environment.GetEnvironmentVariable("LAMBDAFLOW_PIPE_NAME");
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new InvalidOperationException("LAMBDAFLOW_PIPE_NAME is required for NamedPipe.");

            var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(10_000).ConfigureAwait(false);
            return (
                new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true),
                new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true },
                pipe);
        }

        return (Console.In, Console.Out, null);
    }

    private static T? Deserialize<T>(JsonElement? payload) {
        if (payload is null || payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return default;
        return payload.Value.Deserialize<T>(JsonOptions);
    }

    private static JsonElement? SerializePayload(object? payload) {
        return payload is null
            ? null
            : JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonOptions);
    }

    private static string? EntityType(JsonElement? payload) {
        return IsEntity(payload) && payload!.Value.TryGetProperty("$type", out var type)
            ? type.GetString()
            : null;
    }

    private static int? EntityVersion(JsonElement? payload) {
        return IsEntity(payload)
            && payload!.Value.TryGetProperty("$v", out var version)
            && version.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static object ToErrorObject(object error) {
        if (error is LambdaFlowException lambdaFlowError) {
            return new {
                code = lambdaFlowError.Code,
                message = lambdaFlowError.Message,
                details = lambdaFlowError.Details
            };
        }
        if (error is Exception exception)
            return new { code = "HANDLER_ERROR", message = exception.Message };
        if (error is string message)
            return new { code = "ERROR", message };
        return new { code = "ERROR", message = "Unknown error", details = error };
    }

    private static LambdaFlowException ErrorFromEnvelope(Envelope envelope) {
        if (envelope.Error is JsonElement error && error.ValueKind == JsonValueKind.Object) {
            var code = error.TryGetProperty("code", out var codeValue)
                ? codeValue.GetString() ?? "BACKEND_ERROR"
                : "BACKEND_ERROR";
            var message = error.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? "Backend error"
                : "Backend error";
            return new LambdaFlowException(message, code, error.Clone());
        }

        if (envelope.Payload is JsonElement payload
            && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("error", out var legacy)) {
            return new LambdaFlowException(
                legacy.ValueKind == JsonValueKind.String ? legacy.GetString()! : legacy.ToString(),
                "BACKEND_ERROR",
                legacy.Clone());
        }

        return new LambdaFlowException("Backend error.", "BACKEND_ERROR");
    }

    private static bool HasLegacyError(JsonElement? payload) {
        return payload is { ValueKind: JsonValueKind.Object }
            && payload.Value.TryGetProperty("error", out _);
    }

    private static void AssertKind(string? kind) {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("LambdaFlow requires a non-empty kind.", nameof(kind));
    }

    private sealed record HandlerEntry(Func<JsonElement?, Meta, Task<object?>> Handler);
    private sealed record PendingRequest(
        string Kind,
        TaskCompletionSource<JsonElement?> Completion);

    private sealed class Envelope
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("ok")]
        public bool? Ok { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; set; }

        [JsonPropertyName("error")]
        public JsonElement? Error { get; set; }
    }

    public sealed class OntologyEntity<T>
    {
        [JsonPropertyName("$type")]
        public string Type { get; init; } = "";

        [JsonPropertyName("$v")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("data")]
        public T? Data { get; init; }
    }
}

public sealed record Meta(
    string Kind,
    string? Id,
    bool? Ok,
    JsonElement? RawPayload,
    bool IsEntity,
    string? Type,
    int? Version,
    DateTimeOffset ReceivedAt);

public sealed class LambdaFlowException : Exception
{
    public LambdaFlowException(
        string message,
        string code = "LAMBDAFLOW_ERROR",
        object? details = null,
        Exception? innerException = null)
        : base(message, innerException) {
        Code = code;
        Details = details;
    }

    public string Code { get; }
    public object? Details { get; }
}
