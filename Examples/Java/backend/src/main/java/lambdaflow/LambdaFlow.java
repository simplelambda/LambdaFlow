package lambdaflow;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.RandomAccessFile;
import java.io.Reader;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Function;

/**
 * LambdaFlow Java SDK.
 *
 * <p>Drop-in single-file SDK for writing a LambdaFlow backend in Java. Requires
 * Jackson Databind on the classpath (see the example {@code pom.xml}).
 *
 * <h3>Quick start</h3>
 *
 * <pre>{@code
 *   LambdaFlow.receive("uppercase", String.class, text -> text.toUpperCase());
 *   LambdaFlow.receive("describeDog", Dog.class, dog ->
 *       dog.name + " is a " + dog.age + "-year-old " + dog.breed);
 *
 *   LambdaFlow.run();
 * }</pre>
 *
 * <h3>Wire format</h3>
 *
 * One JSON object per line:
 * <pre>{@code
 *   { "kind": "<routing-key>", "id": "<uuid|null>", "payload": <any-json|null> }
 * }</pre>
 *
 * Transport is auto-detected: NamedPipe when {@code LAMBDAFLOW_IPC_TRANSPORT=NamedPipe}
 * (with {@code LAMBDAFLOW_PIPE_NAME}), otherwise stdin/stdout.
 */
public final class LambdaFlow {

    private static final ObjectMapper                MAPPER    = new ObjectMapper();
    private static final Map<String, HandlerEntry>   HANDLERS  = new ConcurrentHashMap<>();
    private static final ExecutorService             POOL      = Executors.newCachedThreadPool(r -> {
        Thread t = new Thread(r, "lambdaflow-handler");
        t.setDaemon(true);
        return t;
    });
    private static final Object                      WRITE_LOCK = new Object();

    private static volatile Transport transport;

    private LambdaFlow() {}

    // -- Public API ---------------------------------------------------------

    /**
     * Register a handler. {@code requestType} is the class to deserialize the
     * incoming payload into; the function's return value is sent back as the
     * response payload (request/response) or as a forwarded event (when no id).
     *
     * <p>Use {@link Void} or {@link Object} as {@code requestType} when the
     * payload is unused.
     */
    public static <TReq, TRes> void receive(String kind, Class<TReq> requestType, Function<TReq, TRes> handler) {
        HANDLERS.put(kind, new HandlerEntry(requestType, handler));
    }

    /** Backward-compatible alias for {@link #receive(String, Class, Function)}. */
    public static <TReq, TRes> void on(String kind, Class<TReq> requestType, Function<TReq, TRes> handler) {
        receive(kind, requestType, handler);
    }

    /** Send an event to the frontend. {@code payload} may be null. */
    public static void send(String kind, Object payload) {
        ObjectNode env = MAPPER.createObjectNode();
        env.put("kind", kind);
        if (payload != null)
            env.set("payload", MAPPER.valueToTree(payload));
        writeEnvelope(env);
    }

    /** Send a bare event with no payload. */
    public static void send(String kind) {
        send(kind, null);
    }

    /** Build an ontology payload: { "$type": "...", "$v": 1, "data": {...} }. */
    public static ObjectNode entity(String type, Object data) {
        return entity(type, data, 1);
    }

    /** Build an ontology payload: { "$type": "...", "$v": version, "data": {...} }. */
    public static ObjectNode entity(String type, Object data, int version) {
        if (type == null || type.isBlank())
            throw new IllegalArgumentException("Entity type must be non-empty.");
        if (version < 1)
            throw new IllegalArgumentException("Entity version must be >= 1.");

        ObjectNode payload = MAPPER.createObjectNode();
        payload.put("$type", type);
        payload.put("$v", version);
        payload.set("data", data == null ? MAPPER.nullNode() : MAPPER.valueToTree(data));
        return payload;
    }

    /** Send an ontology entity payload to the frontend. */
    public static void sendEntity(String kind, String type, Object data) {
        sendEntity(kind, type, data, 1);
    }

    /** Send an ontology entity payload to the frontend. */
    public static void sendEntity(String kind, String type, Object data, int version) {
        send(kind, entity(type, data, version));
    }

    /** Run the message loop. Blocks until the transport closes. */
    public static void run() {
        try {
            transport = openTransport();
        } catch (IOException ex) {
            throw new RuntimeException("Failed to open LambdaFlow transport", ex);
        }

        try {
            String line;
            while ((line = transport.readLine()) != null) {
                final String captured = line.trim();
                if (captured.isEmpty()) continue;
                if (transport.supportsConcurrentReadWrite())
                    POOL.submit(() -> processLine(captured));
                else
                    processLine(captured);
            }
        } catch (IOException ex) {
            System.err.println("[LambdaFlow] read loop failed: " + ex);
        } finally {
            try { transport.close(); } catch (IOException ignored) {}
            POOL.shutdown();
        }
    }

    // -- Internals ----------------------------------------------------------

    private static void processLine(String line) {
        JsonNode envelope;
        try {
            envelope = MAPPER.readTree(line);
        } catch (IOException ex) {
            return;
        }

        String  kind  = envelope.path("kind").asText(null);
        String  id    = envelope.has("id") && !envelope.get("id").isNull() ? envelope.get("id").asText() : null;
        JsonNode pl   = envelope.has("payload") ? envelope.get("payload") : null;
        JsonNode reqPayload = unwrapOntologyPayload(pl);

        if (kind == null) return;
        HandlerEntry entry = HANDLERS.get(kind);
        if (entry == null) return;

        try {
            Object request  = (reqPayload == null || reqPayload.isNull()) ? null : MAPPER.treeToValue(reqPayload, entry.requestType);
            @SuppressWarnings("unchecked")
            Object response = ((Function<Object, Object>) entry.handler).apply(request);

            if (id != null) {
                ObjectNode reply = MAPPER.createObjectNode();
                reply.put("kind", kind);
                reply.put("id", id);
                reply.set("payload", response == null ? MAPPER.nullNode() : MAPPER.valueToTree(response));
                writeEnvelope(reply);
            } else if (response != null) {
                ObjectNode evt = MAPPER.createObjectNode();
                evt.put("kind", kind);
                evt.set("payload", MAPPER.valueToTree(response));
                writeEnvelope(evt);
            }
        } catch (Exception ex) {
            System.err.println("[LambdaFlow] handler '" + kind + "' threw: " + ex);
            if (id != null) {
                ObjectNode err = MAPPER.createObjectNode();
                err.put("kind", kind);
                err.put("id", id);
                ObjectNode body = MAPPER.createObjectNode();
                body.put("error", ex.getMessage());
                err.set("payload", body);
                writeEnvelope(err);
            }
        }
    }

    private static void writeEnvelope(ObjectNode envelope) {
        if (transport == null) return;
        String json;
        try {
            json = MAPPER.writeValueAsString(envelope);
        } catch (IOException ex) {
            return;
        }
        synchronized (WRITE_LOCK) {
            try {
                transport.writeLine(json);
            } catch (IOException ex) {
                System.err.println("[LambdaFlow] write failed: " + ex);
            }
        }
    }

    private static Transport openTransport() throws IOException {
        String mode = System.getenv("LAMBDAFLOW_IPC_TRANSPORT");
        if (mode != null && mode.equalsIgnoreCase("NamedPipe")) {
            String pipeName = System.getenv("LAMBDAFLOW_PIPE_NAME");
            if (pipeName == null || pipeName.isEmpty())
                throw new IOException("LAMBDAFLOW_PIPE_NAME is required for named pipe transport.");
            return new PipeTransport(pipeName);
        }
        return new StdioTransport();
    }

    private static JsonNode unwrapOntologyPayload(JsonNode payload) {
        if (payload == null || !payload.isObject())
            return payload;
        if (!payload.has("$type") || !payload.has("data"))
            return payload;
        return payload.get("data");
    }

    // -- Helpers ------------------------------------------------------------

    private static final class HandlerEntry {
        final Class<?>           requestType;
        final Function<?, ?>     handler;
        HandlerEntry(Class<?> t, Function<?, ?> h) { this.requestType = t; this.handler = h; }
    }

    private interface Transport {
        String readLine() throws IOException;
        void   writeLine(String line) throws IOException;
        void   close() throws IOException;
        boolean supportsConcurrentReadWrite();
    }

    private static final class StdioTransport implements Transport {
        private final BufferedReader in  = new BufferedReader(new InputStreamReader(System.in,  StandardCharsets.UTF_8));
        private final BufferedWriter out = new BufferedWriter(new OutputStreamWriter(System.out, StandardCharsets.UTF_8));

        @Override public String readLine() throws IOException { return in.readLine(); }
        @Override public void   writeLine(String line) throws IOException {
            out.write(line);
            out.newLine();
            out.flush();
        }
        @Override public void close() {}
        @Override public boolean supportsConcurrentReadWrite() { return true; }
    }

    private static final class PipeTransport implements Transport {
        private final RandomAccessFile pipe;

        PipeTransport(String pipeName) throws IOException {
            String path = "\\\\.\\pipe\\" + pipeName;
            long deadline = System.currentTimeMillis() + 10_000L;
            IOException last = null;
            RandomAccessFile opened = null;
            while (System.currentTimeMillis() < deadline) {
                try { opened = new RandomAccessFile(path, "rw"); break; }
                catch (IOException e) {
                    last = e;
                    try { Thread.sleep(100); } catch (InterruptedException ie) { Thread.currentThread().interrupt(); throw new IOException(ie); }
                }
            }
            if (opened == null) throw last != null ? last : new IOException("Could not open pipe " + path);
            this.pipe = opened;
        }

        @Override
        public String readLine() throws IOException {
            java.io.ByteArrayOutputStream buf = new java.io.ByteArrayOutputStream();
            int b;
            while ((b = pipe.read()) != -1) {
                if (b == '\n') return buf.toString(StandardCharsets.UTF_8.name()).replaceAll("\\r$", "");
                buf.write(b);
            }
            return buf.size() == 0 ? null : buf.toString(StandardCharsets.UTF_8.name());
        }

        @Override
        public void writeLine(String line) throws IOException {
            byte[] bytes = line.getBytes(StandardCharsets.UTF_8);
            pipe.write(bytes);
            pipe.write('\n');
        }

        @Override
        public void close() throws IOException {
            pipe.close();
        }

        @Override
        public boolean supportsConcurrentReadWrite() {
            return false;
        }
    }
}
