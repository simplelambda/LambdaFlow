/*!
 * LambdaFlow JavaScript SDK
 * ------------------------
 * Browser-side SDK for LambdaFlow apps.
 *
 * Transport expected from the host:
 *   window.send(string)    frontend -> backend
 *   window.receive(string) backend -> frontend, overwritten by this SDK
 *
 * Wire envelope:
 *   {
 *     kind: string,
 *     id?: string,
 *     ok?: boolean,
 *     payload?: any,
 *     error?: { code?: string, message: string, details?: any }
 *   }
 *
 * Entity payload:
 *   { "$type": string, "$v": number, "data": any }
 */
(function (global) {
    'use strict';

    var SDK_VERSION = '0.2.0';
    var DEFAULT_TIMEOUT_MS = 30000;
    var DEFAULT_REQUEST_RESULT_KIND_SUFFIX = '.result';

    var eventHandlers = new Map();   // kind -> Set<entry>
    var requestHandlers = new Map(); // kind -> handler
    var pending = new Map();         // id -> pending request

    var config = {
        timeoutMs: DEFAULT_TIMEOUT_MS,
        unwrapEntities: true,
        warnOnUnhandled: false,
        logger: global.console || null
    };

    function now() {
        return Date.now ? Date.now() : new Date().getTime();
    }

    function uuid() {
        if (global.crypto && typeof global.crypto.randomUUID === 'function') {
            return global.crypto.randomUUID();
        }

        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }

    function logWarn() {
        if (config.logger && typeof config.logger.warn === 'function') {
            config.logger.warn.apply(config.logger, arguments);
        }
    }

    function logError() {
        if (config.logger && typeof config.logger.error === 'function') {
            config.logger.error.apply(config.logger, arguments);
        }
    }

    function isPlainObject(value) {
        return !!value && typeof value === 'object' && !Array.isArray(value);
    }

    function assertKind(kind) {
        if (typeof kind !== 'string' || kind.trim() === '') {
            throw new Error('LambdaFlow requires a non-empty string kind.');
        }
    }

    function normalizeRequestOptions(timeoutOrOptions) {
        if (typeof timeoutOrOptions === 'number') {
            return { timeoutMs: timeoutOrOptions };
        }
        return timeoutOrOptions || {};
    }

    function toErrorObject(error) {
        if (error instanceof LambdaFlowError) {
            return {
                code: error.code || 'LAMBDAFLOW_ERROR',
                message: error.message,
                details: error.details
            };
        }

        if (error instanceof Error) {
            return {
                code: error.code || 'ERROR',
                message: error.message || String(error),
                details: error.details
            };
        }

        if (typeof error === 'string') {
            return { code: 'ERROR', message: error };
        }

        return {
            code: 'ERROR',
            message: 'Unknown error',
            details: error
        };
    }

    function LambdaFlowError(message, code, details, envelope) {
        this.name = 'LambdaFlowError';
        this.message = message || 'LambdaFlow error';
        this.code = code || 'LAMBDAFLOW_ERROR';
        this.details = details;
        this.envelope = envelope;

        if (Error.captureStackTrace) {
            Error.captureStackTrace(this, LambdaFlowError);
        } else {
            this.stack = (new Error(this.message)).stack;
        }
    }

    LambdaFlowError.prototype = Object.create(Error.prototype);
    LambdaFlowError.prototype.constructor = LambdaFlowError;

    function errorFromEnvelope(envelope) {
        var err = envelope && envelope.error;

        // Backwards compatibility with the first SDK shape:
        // { payload: { error: "..." } }
        if (!err && envelope && envelope.payload && envelope.payload.error) {
            err = envelope.payload.error;
        }

        if (typeof err === 'string') {
            return new LambdaFlowError(err, 'BACKEND_ERROR', undefined, envelope);
        }

        if (isPlainObject(err)) {
            return new LambdaFlowError(
                err.message || 'Backend error',
                err.code || 'BACKEND_ERROR',
                err.details,
                envelope
            );
        }

        return new LambdaFlowError('Backend error', 'BACKEND_ERROR', undefined, envelope);
    }

    function isEntityPayload(payload) {
        return !!payload && typeof payload === 'object'
            && typeof payload.$type === 'string'
            && Object.prototype.hasOwnProperty.call(payload, 'data');
    }

    function unwrapEntityPayload(payload) {
        return isEntityPayload(payload) ? payload.data : payload;
    }

    function createMeta(envelope) {
        var payload = envelope ? envelope.payload : undefined;

        return {
            kind: envelope && envelope.kind,
            id: envelope && envelope.id,
            ok: envelope && envelope.ok,
            rawPayload: payload,
            isEntity: isEntityPayload(payload),
            type: isEntityPayload(payload) ? payload.$type : undefined,
            version: isEntityPayload(payload) ? payload.$v : undefined,
            envelope: envelope,
            receivedAt: now()
        };
    }

    function getDeliveredPayload(envelope, options) {
        var payload = envelope ? envelope.payload : undefined;

        var unwrap = options && Object.prototype.hasOwnProperty.call(options, 'unwrap')
            ? !!options.unwrap
            : !!config.unwrapEntities;

        return unwrap ? unwrapEntityPayload(payload) : payload;
    }

    function validateEnvelope(envelope) {
        return isPlainObject(envelope)
            && typeof envelope.kind === 'string'
            && envelope.kind.trim() !== '';
    }

    function rawSendEnvelope(envelope) {
        if (typeof global.send !== 'function') {
            throw new LambdaFlowError(
                'window.send is not available. This code must run inside a LambdaFlow host, or you must provide a mock transport.',
                'HOST_SEND_UNAVAILABLE'
            );
        }

        if (!validateEnvelope(envelope)) {
            throw new LambdaFlowError('Invalid LambdaFlow envelope.', 'INVALID_ENVELOPE', envelope);
        }

        var raw;

        try {
            raw = JSON.stringify(envelope);
        } catch (error) {
            throw new LambdaFlowError(
                'Could not serialize LambdaFlow envelope. Payload must be JSON-serializable.',
                'SERIALIZATION_FAILED',
                error
            );
        }

        global.send(raw);
        return envelope;
    }

    function settlePending(envelope) {
        if (!envelope.id || !pending.has(envelope.id)) {
            return false;
        }

        var entry = pending.get(envelope.id);
        pending.delete(envelope.id);
        clearTimeout(entry.timer);

        if (entry.abortCleanup) {
            entry.abortCleanup();
        }

        if (envelope.ok === false || (envelope.payload && envelope.payload.error)) {
            entry.reject(errorFromEnvelope(envelope));
            return true;
        }

        try {
            entry.resolve(getDeliveredPayload(envelope, { unwrap: entry.unwrap }));
        } catch (error) {
            entry.reject(error);
        }

        return true;
    }

    function getEventEntries(kind) {
        var entries = [];
        var exact = eventHandlers.get(kind);
        var wildcard = eventHandlers.get('*');

        if (exact) {
            exact.forEach(function (entry) {
                entries.push(entry);
            });
        }

        if (wildcard) {
            wildcard.forEach(function (entry) {
                entries.push(entry);
            });
        }

        return entries;
    }

    function dispatchEvent(envelope) {
        var entries = getEventEntries(envelope.kind);

        if (entries.length === 0) {
            if (config.warnOnUnhandled) {
                logWarn('[LambdaFlow] unhandled event:', envelope.kind, envelope);
            }
            return false;
        }

        entries.forEach(function (entry) {
            try {
                var payload = getDeliveredPayload(envelope, { unwrap: entry.unwrap });
                var meta = createMeta(envelope);

                entry.handler(payload, meta);
            } catch (error) {
                logError('[LambdaFlow] handler "' + envelope.kind + '" threw:', error);
            } finally {
                if (entry.once) {
                    LambdaFlow.off(entry.kind, entry.handler);
                }
            }
        });

        return true;
    }

    function dispatchInboundRequest(envelope) {
        if (!envelope.id || !requestHandlers.has(envelope.kind)) {
            return false;
        }

        var handler = requestHandlers.get(envelope.kind);
        var payload = getDeliveredPayload(envelope, { unwrap: handler.unwrap });
        var meta = createMeta(envelope);

        Promise.resolve()
            .then(function () {
                return handler.fn(payload, meta);
            })
            .then(function (result) {
                var responseKind = envelope.kind + DEFAULT_REQUEST_RESULT_KIND_SUFFIX;

                rawSendEnvelope({
                    kind: responseKind,
                    id: envelope.id,
                    ok: true,
                    payload: result === undefined ? null : result
                });
            })
            .catch(function (error) {
                var responseKind = envelope.kind + DEFAULT_REQUEST_RESULT_KIND_SUFFIX;

                rawSendEnvelope({
                    kind: responseKind,
                    id: envelope.id,
                    ok: false,
                    error: toErrorObject(error)
                });
            });

        return true;
    }

    function receiveRawMessage(raw) {
        var envelope;

        try {
            envelope = typeof raw === 'string' ? JSON.parse(raw) : raw;
        } catch (error) {
            logWarn('[LambdaFlow] received non-JSON message:', raw);
            return;
        }

        if (!validateEnvelope(envelope)) {
            logWarn('[LambdaFlow] received invalid envelope:', envelope);
            return;
        }

        if (settlePending(envelope)) {
            return;
        }

        if (dispatchInboundRequest(envelope)) {
            return;
        }

        dispatchEvent(envelope);
    }

    function addEventHandler(kind, handler, options) {
        assertKind(kind);

        if (typeof handler !== 'function') {
            throw new Error('LambdaFlow.on requires a handler function.');
        }

        options = options || {};

        var set = eventHandlers.get(kind);

        if (!set) {
            set = new Set();
            eventHandlers.set(kind, set);
        }

        var entry = {
            kind: kind,
            handler: handler,
            once: !!options.once,
            unwrap: Object.prototype.hasOwnProperty.call(options, 'unwrap')
                ? !!options.unwrap
                : config.unwrapEntities
        };

        set.add(entry);

        return function unsubscribe() {
            LambdaFlow.off(kind, handler);
        };
    }

    function cleanupPendingRequest(id, reason) {
        var entry = pending.get(id);

        if (!entry) {
            return;
        }

        pending.delete(id);
        clearTimeout(entry.timer);

        if (entry.abortCleanup) {
            entry.abortCleanup();
        }

        entry.reject(reason);
    }

    var LambdaFlow = {
        version: SDK_VERSION,
        Error: LambdaFlowError,

        configure: function (options) {
            options = options || {};

            if (typeof options.timeoutMs === 'number' && options.timeoutMs > 0) {
                config.timeoutMs = options.timeoutMs;
            }

            if (typeof options.unwrapEntities === 'boolean') {
                config.unwrapEntities = options.unwrapEntities;
            }

            if (typeof options.warnOnUnhandled === 'boolean') {
                config.warnOnUnhandled = options.warnOnUnhandled;
            }

            if (Object.prototype.hasOwnProperty.call(options, 'logger')) {
                config.logger = options.logger;
            }

            if (typeof options.transportSend === 'function') {
                global.send = options.transportSend;
            }

            return this;
        },

        isHostAvailable: function () {
            return typeof global.send === 'function';
        },

        /**
         * Low-level parser entrypoint used by the host through window.receive(raw).
         */
        _receiveRaw: receiveRawMessage,

        /**
         * Register one or more listeners for an inbound event.
         * Returns unsubscribe().
         */
        on: function (kind, handler, options) {
            return addEventHandler(kind, handler, options);
        },

        /**
         * Alias of on(kind, handler, options).
         */
        receive: function (kind, handler, options) {
            return addEventHandler(kind, handler, options);
        },

        /**
         * Register a listener that runs once.
         * Returns unsubscribe().
         */
        once: function (kind, handler, options) {
            options = options || {};
            options.once = true;

            return addEventHandler(kind, handler, options);
        },

        /**
         * Register a request handler for backend -> frontend calls.
         */
        handle: function (kind, handler, options) {
            assertKind(kind);

            if (typeof handler !== 'function') {
                throw new Error('LambdaFlow.handle requires a handler function.');
            }

            options = options || {};

            requestHandlers.set(kind, {
                fn: handler,
                unwrap: Object.prototype.hasOwnProperty.call(options, 'unwrap')
                    ? !!options.unwrap
                    : config.unwrapEntities
            });

            return function unregister() {
                LambdaFlow.unhandle(kind);
            };
        },

        unhandle: function (kind) {
            requestHandlers.delete(kind);
            return this;
        },

        /**
         * Remove listeners.
         * If handler is omitted, removes all listeners for that kind.
         */
        off: function (kind, handler) {
            if (!eventHandlers.has(kind)) {
                return this;
            }

            if (!handler) {
                eventHandlers.delete(kind);
                return this;
            }

            var set = eventHandlers.get(kind);

            set.forEach(function (entry) {
                if (entry.handler === handler) {
                    set.delete(entry);
                }
            });

            if (set.size === 0) {
                eventHandlers.delete(kind);
            }

            return this;
        },

        clearHandlers: function () {
            eventHandlers.clear();
            requestHandlers.clear();
            return this;
        },

        /**
         * Fire-and-forget message to the backend.
         */
        send: function (kind, payload, options) {
            assertKind(kind);
            options = options || {};

            return rawSendEnvelope({
                kind: kind,
                id: options.id,
                ok: options.ok,
                payload: payload === undefined ? null : payload
            });
        },

        emit: function (kind, payload, options) {
            return this.send(kind, payload, options);
        },

        /**
         * Send a successful response for a manually handled request.
         */
        respond: function (kind, id, payload) {
            assertKind(kind);

            if (!id) {
                throw new Error('LambdaFlow.respond requires the request id.');
            }

            return rawSendEnvelope({
                kind: kind,
                id: id,
                ok: true,
                payload: payload === undefined ? null : payload
            });
        },

        /**
         * Send an error response for a manually handled request.
         */
        reject: function (kind, id, error) {
            assertKind(kind);

            if (!id) {
                throw new Error('LambdaFlow.reject requires the request id.');
            }

            return rawSendEnvelope({
                kind: kind,
                id: id,
                ok: false,
                error: toErrorObject(error)
            });
        },

        /**
         * Request/response call.
         *
         * Third argument can be:
         *   timeoutMs: number
         *   options: { timeoutMs?, signal?, unwrap?, id? }
         */
        request: function (kind, payload, timeoutOrOptions) {
            assertKind(kind);

            var options = normalizeRequestOptions(timeoutOrOptions);
            var id = options.id || uuid();

            var timeoutMs = typeof options.timeoutMs === 'number' && options.timeoutMs > 0
                ? options.timeoutMs
                : config.timeoutMs;

            var unwrap = Object.prototype.hasOwnProperty.call(options, 'unwrap')
                ? !!options.unwrap
                : config.unwrapEntities;

            if (pending.has(id)) {
                return Promise.reject(
                    new LambdaFlowError(
                        'Duplicate request id.',
                        'DUPLICATE_REQUEST_ID',
                        { id: id }
                    )
                );
            }

            return new Promise(function (resolve, reject) {
                var timer = setTimeout(function () {
                    cleanupPendingRequest(
                        id,
                        new LambdaFlowError(
                            'LambdaFlow request "' + kind + '" timed out.',
                            'REQUEST_TIMEOUT',
                            {
                                kind: kind,
                                id: id,
                                timeoutMs: timeoutMs
                            }
                        )
                    );
                }, timeoutMs);

                var abortCleanup = null;

                if (options.signal) {
                    if (options.signal.aborted) {
                        clearTimeout(timer);

                        reject(
                            new LambdaFlowError(
                                'LambdaFlow request "' + kind + '" was aborted.',
                                'REQUEST_ABORTED',
                                {
                                    kind: kind,
                                    id: id
                                }
                            )
                        );

                        return;
                    }

                    var abortHandler = function () {
                        cleanupPendingRequest(
                            id,
                            new LambdaFlowError(
                                'LambdaFlow request "' + kind + '" was aborted.',
                                'REQUEST_ABORTED',
                                {
                                    kind: kind,
                                    id: id
                                }
                            )
                        );
                    };

                    options.signal.addEventListener('abort', abortHandler, { once: true });

                    abortCleanup = function () {
                        options.signal.removeEventListener('abort', abortHandler);
                    };
                }

                pending.set(id, {
                    kind: kind,
                    resolve: resolve,
                    reject: reject,
                    timer: timer,
                    unwrap: unwrap,
                    abortCleanup: abortCleanup
                });

                try {
                    rawSendEnvelope({
                        kind: kind,
                        id: id,
                        payload: payload === undefined ? null : payload
                    });
                } catch (error) {
                    cleanupPendingRequest(id, error);
                }
            });
        },

        /**
         * Build an entity payload.
         */
        entity: function (type, data, version) {
            if (typeof type !== 'string' || type.trim() === '') {
                throw new Error('LambdaFlow.entity requires a non-empty type string.');
            }

            var v = version === undefined ? 1 : version;

            if (!Number.isInteger(v) || v < 1) {
                throw new Error('LambdaFlow.entity version must be an integer >= 1.');
            }

            return {
                $type: type,
                $v: v,
                data: data === undefined ? null : data
            };
        },

        isEntity: isEntityPayload,

        unwrapEntity: unwrapEntityPayload,

        entityType: function (payload) {
            return isEntityPayload(payload) ? payload.$type : undefined;
        },

        entityVersion: function (payload) {
            return isEntityPayload(payload) ? payload.$v : undefined;
        },

        sendEntity: function (kind, type, data, version, options) {
            return this.send(kind, this.entity(type, data, version), options);
        },

        requestEntity: function (kind, type, data, timeoutOrOptions, version) {
            return this.request(
                kind,
                this.entity(type, data, version),
                timeoutOrOptions
            );
        },

        pendingCount: function () {
            return pending.size;
        },

        destroy: function () {
            eventHandlers.clear();
            requestHandlers.clear();

            pending.forEach(function (entry, id) {
                clearTimeout(entry.timer);

                if (entry.abortCleanup) {
                    entry.abortCleanup();
                }

                entry.reject(
                    new LambdaFlowError(
                        'LambdaFlow SDK destroyed.',
                        'SDK_DESTROYED',
                        { id: id }
                    )
                );
            });

            pending.clear();
        }
    };

    global.receive = receiveRawMessage;
    global.LambdaFlow = LambdaFlow;

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = LambdaFlow;
    }
})(typeof window !== 'undefined' ? window : globalThis);