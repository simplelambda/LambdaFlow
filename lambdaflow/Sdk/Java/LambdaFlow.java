package lambdaflow;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.RandomAccessFile;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.Function;

/**
 * Single-file LambdaFlow backend SDK.
 *
 * <p>Common API across backend SDKs: configure, receive/on/handle, unhandle,
 * send/emit, request, respond/reject, entity helpers, run/stop and
 * pendingCount.
 */
public final class LambdaFlow {
    public static final String VERSION = "1.3.0";


    private static final String RESULT_SUFFIX = ".result";
    private static final ObjectMapper MAPPER = new ObjectMapper();
    private static final Map<String, HandlerEntry> HANDLERS = new ConcurrentHashMap<>();
    private static final Map<String, PendingRequest> PENDING = new ConcurrentHashMap<>();
    private static final Object WRITE_LOCK = new Object();
    private static final ExecutorService POOL = Executors.newCachedThreadPool(r -> {
        Thread thread = new Thread(r, "lambdaflow-handler");
        thread.setDaemon(true);
        return thread;
    });
    private static final ScheduledExecutorService TIMERS = Executors.newSingleThreadScheduledExecutor(r -> {
        Thread thread = new Thread(r, "lambdaflow-timer");
        thread.setDaemon(true);
        return thread;
    });

    private static volatile Transport transport;
    private static volatile boolean running;
    private static volatile int defaultTimeoutMs = 30_000;
    private static volatile boolean unwrapEntities = true;
    private static volatile boolean warnOnUnhandled;
    private static volatile boolean replyToEvents;

    private LambdaFlow() {}

    public static void configure(
            Integer timeoutMs,
            Boolean shouldUnwrapEntities,
            Boolean shouldWarnOnUnhandled,
            Boolean shouldReplyToEvents) {
        if (timeoutMs != null) {
            if (timeoutMs <= 0)
                throw new IllegalArgumentException("timeoutMs must be greater than zero.");
            defaultTimeoutMs = timeoutMs;
        }
        if (shouldUnwrapEntities != null) unwrapEntities = shouldUnwrapEntities;
        if (shouldWarnOnUnhandled != null) warnOnUnhandled = shouldWarnOnUnhandled;
        if (shouldReplyToEvents != null) replyToEvents = shouldReplyToEvents;
    }

    public static <TReq, TRes> void receive(
            String kind,
            Class<TReq> requestType,
            Function<TReq, TRes> handler) {
        assertKind(kind);
        if (requestType == null || handler == null)
            throw new IllegalArgumentException("requestType and handler are required.");
        HANDLERS.put(kind, new HandlerEntry(requestType, handler));
    }

    public static <TReq, TRes> void on(
            String kind,
            Class<TReq> requestType,
            Function<TReq, TRes> handler) {
        receive(kind, requestType, handler);
    }

    public static <TReq, TRes> void handle(
            String kind,
            Class<TReq> requestType,
            Function<TReq, TRes> handler) {
        receive(kind, requestType, handler);
    }

    public static boolean unhandle(String kind) {
        assertKind(kind);
        return HANDLERS.remove(kind) != null;
    }

    public static boolean off(String kind) {
        return unhandle(kind);
    }

    public static void send(String kind, Object payload) {
        assertKind(kind);
        ObjectNode envelope = MAPPER.createObjectNode();
        envelope.put("kind", kind);
        envelope.set("payload", payload == null ? MAPPER.nullNode() : MAPPER.valueToTree(payload));
        writeEnvelope(envelope);
    }

    public static void send(String kind) {
        send(kind, null);
    }

    public static void emit(String kind, Object payload) {
        send(kind, payload);
    }

    public static void emit(String kind) {
        send(kind);
    }

    public static <T> CompletableFuture<T> requestAsync(
            String kind,
            Object payload,
            Class<T> responseType) {
        return requestAsync(kind, payload, responseType, defaultTimeoutMs);
    }

    public static <T> CompletableFuture<T> requestAsync(
            String kind,
            Object payload,
            Class<T> responseType,
            int timeoutMs) {
        assertKind(kind);
        if (responseType == null)
            throw new IllegalArgumentException("responseType is required.");
        if (timeoutMs <= 0)
            throw new IllegalArgumentException("timeoutMs must be greater than zero.");

        String id = UUID.randomUUID().toString();
        CompletableFuture<Object> rawFuture = new CompletableFuture<>();
        PendingRequest pending = new PendingRequest(kind, responseType, rawFuture);
        PENDING.put(id, pending);

        ObjectNode envelope = MAPPER.createObjectNode();
        envelope.put("kind", kind);
        envelope.put("id", id);
        envelope.set("payload", payload == null ? MAPPER.nullNode() : MAPPER.valueToTree(payload));

        try {
            writeEnvelope(envelope);
        } catch (RuntimeException ex) {
            PENDING.remove(id);
            rawFuture.completeExceptionally(ex);
        }

        TIMERS.schedule(() -> {
            PendingRequest timedOut = PENDING.remove(id);
            if (timedOut != null) {
                timedOut.future.completeExceptionally(new LambdaFlowException(
                    "Request \"" + kind + "\" timed out.",
                    "REQUEST_TIMEOUT",
                    Map.of("kind", kind, "id", id, "timeoutMs", timeoutMs)));
            }
        }, timeoutMs, TimeUnit.MILLISECONDS);

        return rawFuture.thenApply(responseType::cast);
    }

    public static <T> T request(
            String kind,
            Object payload,
            Class<T> responseType) {
        return request(kind, payload, responseType, defaultTimeoutMs);
    }

    public static <T> T request(
            String kind,
            Object payload,
            Class<T> responseType,
            int timeoutMs) {
        try {
            return requestAsync(kind, payload, responseType, timeoutMs).get();
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt();
            throw new LambdaFlowException("Request interrupted.", "REQUEST_INTERRUPTED", null, ex);
        } catch (ExecutionException ex) {
            Throwable cause = ex.getCause();
            if (cause instanceof RuntimeException)
                throw (RuntimeException) cause;
            throw new LambdaFlowException("Request failed.", "REQUEST_FAILED", null, cause);
        }
    }

    public static void respond(String kind, String id, Object payload) {
        assertKind(kind);
        if (id == null || id.isBlank())
            throw new IllegalArgumentException("respond requires a request id.");
        ObjectNode envelope = MAPPER.createObjectNode();
        envelope.put("kind", resultKind(kind));
        envelope.put("id", id);
        envelope.put("ok", true);
        envelope.set("payload", payload == null ? MAPPER.nullNode() : MAPPER.valueToTree(payload));
        writeEnvelope(envelope);
    }

    public static void reject(String kind, String id, Object error) {
        assertKind(kind);
        if (id == null || id.isBlank())
            throw new IllegalArgumentException("reject requires a request id.");
        ObjectNode envelope = MAPPER.createObjectNode();
        envelope.put("kind", resultKind(kind));
        envelope.put("id", id);
        envelope.put("ok", false);
        envelope.set("error", errorObject(error));
        writeEnvelope(envelope);
    }

    public static ObjectNode entity(String type, Object data) {
        return entity(type, data, 1);
    }

    public static ObjectNode entity(String type, Object data, int version) {
        if (type == null || type.isBlank())
            throw new IllegalArgumentException("Entity type must be non-empty.");
        if (version < 1)
            throw new IllegalArgumentException("Entity version must be at least 1.");

        ObjectNode result = MAPPER.createObjectNode();
        result.put("$type", type);
        result.put("$v", version);
        result.set("data", data == null ? MAPPER.nullNode() : MAPPER.valueToTree(data));
        return result;
    }

    public static boolean isEntity(JsonNode payload) {
        return payload != null
            && payload.isObject()
            && payload.path("$type").isTextual()
            && payload.has("data");
    }

    public static JsonNode unwrapEntity(JsonNode payload) {
        return isEntity(payload) ? payload.get("data") : payload;
    }

    public static void sendEntity(String kind, String type, Object data) {
        sendEntity(kind, type, data, 1);
    }

    public static void sendEntity(String kind, String type, Object data, int version) {
        send(kind, entity(type, data, version));
    }

    public static <T> CompletableFuture<T> requestEntityAsync(
            String kind,
            String type,
            Object data,
            Class<T> responseType,
            int version,
            int timeoutMs) {
        return requestAsync(kind, entity(type, data, version), responseType, timeoutMs);
    }

    public static void run() {
        List<CompletableFuture<Void>> work = new ArrayList<>();
        synchronized (LambdaFlow.class) {
            if (running)
                throw new IllegalStateException("LambdaFlow is already running.");
            try {
                transport = openTransport();
            } catch (IOException ex) {
                throw new LambdaFlowException(
                    "Failed to open LambdaFlow transport.",
                    "TRANSPORT_OPEN_FAILED",
                    null,
                    ex);
            }
            running = true;
        }

        try {
            String line;
            while (running && (line = transport.readLine()) != null) {
                String captured = line.trim();
                if (!captured.isEmpty())
                    work.add(CompletableFuture.runAsync(() -> processLine(captured), POOL));
            }
        } catch (IOException ex) {
            if (running)
                System.err.println("[LambdaFlow] read loop failed: " + ex.getMessage());
        } finally {
            try {
                CompletableFuture.allOf(work.toArray(new CompletableFuture<?>[0]))
                    .get(10, TimeUnit.SECONDS);
            } catch (InterruptedException ex) {
                Thread.currentThread().interrupt();
            } catch (ExecutionException | TimeoutException ignored) {}
            stop();
        }
    }

    public static void stop() {
        running = false;
        Transport current = transport;
        transport = null;
        if (current != null) {
            try {
                current.close();
            } catch (IOException ignored) {}
        }
        for (Map.Entry<String, PendingRequest> item : PENDING.entrySet()) {
            PendingRequest pending = PENDING.remove(item.getKey());
            if (pending != null)
                pending.future.completeExceptionally(
                    new LambdaFlowException("LambdaFlow stopped.", "SDK_STOPPED"));
        }
    }

    public static int pendingCount() {
        return PENDING.size();
    }

    private static void processLine(String line) {
        JsonNode envelope;
        try {
            envelope = MAPPER.readTree(line);
        } catch (IOException ex) {
            System.err.println("[LambdaFlow] ignored malformed JSON.");
            return;
        }

        if (envelope == null || !envelope.path("kind").isTextual())
            return;
        String kind = envelope.path("kind").asText();
        String id = envelope.path("id").isTextual() ? envelope.path("id").asText() : null;
        JsonNode payload = envelope.has("payload") ? envelope.get("payload") : null;

        if (id != null) {
            PendingRequest pending = PENDING.remove(id);
            if (pending != null) {
                settlePending(pending, envelope, payload);
                return;
            }
        }

        HandlerEntry entry = HANDLERS.get(kind);
        if (entry == null) {
            if (warnOnUnhandled)
                System.err.println("[LambdaFlow] no handler for '" + kind + "'.");
            return;
        }

        JsonNode delivered = unwrapEntities ? unwrapEntity(payload) : payload;
        Meta meta = new Meta(
            kind,
            id,
            envelope.has("ok") ? envelope.path("ok").asBoolean() : null,
            payload,
            isEntity(payload),
            isEntity(payload) ? payload.path("$type").asText(null) : null,
            isEntity(payload) && payload.path("$v").canConvertToInt()
                ? payload.path("$v").asInt()
                : null,
            Instant.now());

        try {
            Object request = convert(delivered, entry.requestType);
            @SuppressWarnings("unchecked")
            Object response = ((Function<Object, Object>) entry.handler).apply(request);
            if (id != null)
                respond(kind, id, response);
            else if (replyToEvents && response != null)
                send(kind, response);
        } catch (Exception ex) {
            System.err.println("[LambdaFlow] handler '" + kind + "' failed: " + ex.getMessage());
            if (id != null)
                reject(kind, id, ex);
        }
    }

    private static void settlePending(
            PendingRequest pending,
            JsonNode envelope,
            JsonNode payload) {
        if ((envelope.has("ok") && !envelope.path("ok").asBoolean())
                || envelope.has("error")
                || (payload != null && payload.isObject() && payload.has("error"))) {
            pending.future.completeExceptionally(errorFromEnvelope(envelope, payload));
            return;
        }

        JsonNode delivered = unwrapEntities ? unwrapEntity(payload) : payload;
        try {
            pending.future.complete(convert(delivered, pending.responseType));
        } catch (Exception ex) {
            pending.future.completeExceptionally(ex);
        }
    }

    private static Object convert(JsonNode value, Class<?> type) throws IOException {
        if (value == null || value.isNull())
            return null;
        if (JsonNode.class.isAssignableFrom(type))
            return value;
        return MAPPER.treeToValue(value, type);
    }

    private static void writeEnvelope(ObjectNode envelope) {
        Transport current = transport;
        if (current == null)
            throw new IllegalStateException(
                "LambdaFlow transport is not running. Call run before sending.");
        String json;
        try {
            json = MAPPER.writeValueAsString(envelope);
        } catch (IOException ex) {
            throw new LambdaFlowException(
                "Could not serialize LambdaFlow envelope.",
                "SERIALIZATION_FAILED",
                null,
                ex);
        }

        synchronized (WRITE_LOCK) {
            try {
                current.writeLine(json);
            } catch (IOException ex) {
                throw new LambdaFlowException(
                    "Could not write LambdaFlow envelope.",
                    "TRANSPORT_WRITE_FAILED",
                    null,
                    ex);
            }
        }
    }

    private static Transport openTransport() throws IOException {
        String mode = System.getenv("LAMBDAFLOW_IPC_TRANSPORT");
        if (mode != null && mode.equalsIgnoreCase("NamedPipe")) {
            String pipeName = System.getenv("LAMBDAFLOW_PIPE_NAME");
            if (pipeName == null || pipeName.isBlank())
                throw new IOException("LAMBDAFLOW_PIPE_NAME is required for NamedPipe.");
            return new PipeTransport(pipeName);
        }
        return new StdioTransport();
    }

    private static ObjectNode errorObject(Object error) {
        ObjectNode result = MAPPER.createObjectNode();
        if (error instanceof LambdaFlowException) {
            LambdaFlowException lambdaFlowError = (LambdaFlowException) error;
            result.put("code", lambdaFlowError.getCode());
            result.put("message", lambdaFlowError.getMessage());
            if (lambdaFlowError.getDetails() != null)
                result.set("details", MAPPER.valueToTree(lambdaFlowError.getDetails()));
        } else if (error instanceof Throwable) {
            result.put("code", "HANDLER_ERROR");
            result.put("message", ((Throwable) error).getMessage());
        } else if (error instanceof String) {
            result.put("code", "ERROR");
            result.put("message", (String) error);
        } else {
            result.put("code", "ERROR");
            result.put("message", "Unknown error");
            result.set("details", MAPPER.valueToTree(error));
        }
        return result;
    }

    private static LambdaFlowException errorFromEnvelope(JsonNode envelope, JsonNode payload) {
        JsonNode error = envelope.get("error");
        if (error != null && error.isObject()) {
            return new LambdaFlowException(
                error.path("message").asText("Backend error"),
                error.path("code").asText("BACKEND_ERROR"),
                error.get("details"));
        }
        if (payload != null && payload.isObject() && payload.has("error")) {
            return new LambdaFlowException(
                payload.path("error").asText("Backend error"),
                "BACKEND_ERROR",
                payload.get("error"));
        }
        return new LambdaFlowException("Backend error.", "BACKEND_ERROR");
    }

    private static String resultKind(String kind) {
        return kind.endsWith(RESULT_SUFFIX) ? kind : kind + RESULT_SUFFIX;
    }

    private static void assertKind(String kind) {
        if (kind == null || kind.isBlank())
            throw new IllegalArgumentException("LambdaFlow requires a non-empty kind.");
    }

    private static final class HandlerEntry {
        final Class<?> requestType;
        final Function<?, ?> handler;

        HandlerEntry(Class<?> requestType, Function<?, ?> handler) {
            this.requestType = requestType;
            this.handler = handler;
        }
    }

    private static final class PendingRequest {
        final String kind;
        final Class<?> responseType;
        final CompletableFuture<Object> future;

        PendingRequest(String kind, Class<?> responseType, CompletableFuture<Object> future) {
            this.kind = kind;
            this.responseType = responseType;
            this.future = future;
        }
    }

    private interface Transport {
        String readLine() throws IOException;
        void writeLine(String line) throws IOException;
        void close() throws IOException;
    }

    private static final class StdioTransport implements Transport {
        private final BufferedReader in = new BufferedReader(
            new InputStreamReader(System.in, StandardCharsets.UTF_8));
        private final BufferedWriter out = new BufferedWriter(
            new OutputStreamWriter(System.out, StandardCharsets.UTF_8));

        @Override
        public String readLine() throws IOException {
            return in.readLine();
        }

        @Override
        public void writeLine(String line) throws IOException {
            out.write(line);
            out.newLine();
            out.flush();
        }

        @Override
        public void close() {}
    }

    private static final class PipeTransport implements Transport {
        private final RandomAccessFile pipe;

        PipeTransport(String pipeName) throws IOException {
            String path = "\\\\.\\pipe\\" + pipeName;
            long deadline = System.currentTimeMillis() + 10_000;
            IOException last = null;
            RandomAccessFile opened = null;
            while (System.currentTimeMillis() < deadline) {
                try {
                    opened = new RandomAccessFile(path, "rw");
                    break;
                } catch (IOException ex) {
                    last = ex;
                    try {
                        Thread.sleep(100);
                    } catch (InterruptedException interrupted) {
                        Thread.currentThread().interrupt();
                        throw new IOException(interrupted);
                    }
                }
            }
            if (opened == null)
                throw last != null ? last : new IOException("Could not open named pipe.");
            pipe = opened;
        }

        @Override
        public String readLine() throws IOException {
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            int value;
            while ((value = pipe.read()) != -1) {
                if (value == '\n') {
                    String line = buffer.toString(StandardCharsets.UTF_8);
                    return line.endsWith("\r") ? line.substring(0, line.length() - 1) : line;
                }
                buffer.write(value);
            }
            return buffer.size() == 0 ? null : buffer.toString(StandardCharsets.UTF_8);
        }

        @Override
        public void writeLine(String line) throws IOException {
            pipe.write(line.getBytes(StandardCharsets.UTF_8));
            pipe.write('\n');
        }

        @Override
        public void close() throws IOException {
            pipe.close();
        }
    }

    public static final class Meta {
        public final String kind;
        public final String id;
        public final Boolean ok;
        public final JsonNode rawPayload;
        public final boolean entity;
        public final String type;
        public final Integer version;
        public final Instant receivedAt;

        Meta(
                String kind,
                String id,
                Boolean ok,
                JsonNode rawPayload,
                boolean entity,
                String type,
                Integer version,
                Instant receivedAt) {
            this.kind = kind;
            this.id = id;
            this.ok = ok;
            this.rawPayload = rawPayload;
            this.entity = entity;
            this.type = type;
            this.version = version;
            this.receivedAt = receivedAt;
        }
    }

    public static class LambdaFlowException extends RuntimeException {
        private final String code;
        private final Object details;

        public LambdaFlowException(String message, String code) {
            this(message, code, null, null);
        }

        public LambdaFlowException(String message, String code, Object details) {
            this(message, code, details, null);
        }

        public LambdaFlowException(
                String message,
                String code,
                Object details,
                Throwable cause) {
            super(message, cause);
            this.code = code;
            this.details = details;
        }

        public String getCode() {
            return code;
        }

        public Object getDetails() {
            return details;
        }
    }
}
