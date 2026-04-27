/*!
 * LambdaFlow JavaScript SDK
 * ------------------------
 * Drop-in browser-side SDK for LambdaFlow apps.
 *
 * Quick start:
 *
 *     <script src="lambdaflow.js"></script>
 *     <script>
 *         LambdaFlow.receive('echo', payload => console.log('got', payload));
 *         LambdaFlow.send('greet', { name: 'world' });
 *
 *         LambdaFlow.requestEntity('describeDog', 'animals.dog', {
 *             name: 'Rex', age: 4, breed: 'Labrador'
 *         })
 *             .then(res => console.log(res.text));
 *     </script>
 *
 * Wire format (one JSON object per line, between front and back):
 *     { kind: <routing-key>, id?: <uuid>, payload?: <any-json> }
 *
 * Sits on top of the host-injected globals `window.send(string)` and
 * `window.receive(string)`.
 */
(function (global) {
    'use strict';

    var handlers = new Map();
    var pending  = new Map();

    function uuid() {
        if (global.crypto && typeof global.crypto.randomUUID === 'function')
            return global.crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }

    function rawSend(envelope) {
        if (typeof global.send !== 'function') {
            console.warn('[LambdaFlow] window.send is not available; is this running inside a LambdaFlow host?');
            return;
        }
        global.send(JSON.stringify(envelope));
    }

    function isEntityPayload(payload) {
        return !!payload && typeof payload === 'object'
            && typeof payload.$type === 'string'
            && Object.prototype.hasOwnProperty.call(payload, 'data');
    }

    function unwrapEntityPayload(payload) {
        return isEntityPayload(payload) ? payload.data : payload;
    }

    global.receive = function (raw) {
        var envelope;
        try { envelope = JSON.parse(raw); }
        catch (e) { console.warn('[LambdaFlow] received non-JSON:', raw); return; }

        if (!envelope || typeof envelope.kind !== 'string') return;

        // Response to a pending request?
        if (envelope.id && pending.has(envelope.id)) {
            var entry = pending.get(envelope.id);
            pending.delete(envelope.id);
            clearTimeout(entry.timer);
            if (envelope.payload && envelope.payload.error)
                entry.reject(new Error(envelope.payload.error));
            else
                entry.resolve(unwrapEntityPayload(envelope.payload));
            return;
        }

        // Backend-initiated event?
        var handler = handlers.get(envelope.kind);
        if (handler) {
            try { handler(unwrapEntityPayload(envelope.payload)); }
            catch (e) { console.error('[LambdaFlow] handler "' + envelope.kind + '" threw:', e); }
        }
    };

    var LambdaFlow = {
        /** Register a handler for an inbound event with the given kind. */
        on: function (kind, handler) {
            handlers.set(kind, handler);
            return this;
        },

        /** Alias of on(kind, handler), kept for naming symmetry with backend SDKs. */
        receive: function (kind, handler) {
            return this.on(kind, handler);
        },

        /** Remove a previously registered handler. */
        off: function (kind) {
            handlers.delete(kind);
            return this;
        },

        /** Fire-and-forget message to the backend. */
        send: function (kind, payload) {
            rawSend({ kind: kind, payload: payload === undefined ? null : payload });
        },

        /** Build an ontology payload: { "$type": "...", "$v": 1, "data": {...} }. */
        entity: function (type, data, version) {
            if (!type || typeof type !== 'string')
                throw new Error('LambdaFlow.entity requires a non-empty type string');
            var v = version || 1;
            if (v < 1)
                throw new Error('LambdaFlow.entity version must be >= 1');
            return { $type: type, $v: v, data: data };
        },

        /** Return true when payload matches the LambdaFlow Entity v1 shape. */
        isEntity: function (payload) {
            return isEntityPayload(payload);
        },

        /** Return entity.data when payload is an entity, otherwise return payload as-is. */
        unwrapEntity: function (payload) {
            return unwrapEntityPayload(payload);
        },

        /** Fire-and-forget send with an ontology entity payload. */
        sendEntity: function (kind, type, data, version) {
            return this.send(kind, this.entity(type, data, version));
        },

        /**
         * Send a request to the backend and return a Promise that resolves with
         * the response payload. Rejects if the backend doesn't reply within
         * `timeoutMs` (default 30s) or if the response payload contains an
         * `error` field.
         */
        request: function (kind, payload, timeoutMs) {
            var id = uuid();
            return new Promise(function (resolve, reject) {
                var timer = setTimeout(function () {
                    pending.delete(id);
                    reject(new Error('LambdaFlow request "' + kind + '" timed out'));
                }, timeoutMs || 30000);
                pending.set(id, { resolve: resolve, reject: reject, timer: timer });
                rawSend({ kind: kind, id: id, payload: payload === undefined ? null : payload });
            });
        },

        /** Request/response helper with ontology entity payload. */
        requestEntity: function (kind, type, data, timeoutMs, version) {
            return this.request(kind, this.entity(type, data, version), timeoutMs);
        }
    };

    global.LambdaFlow = LambdaFlow;
})(typeof window !== 'undefined' ? window : globalThis);
