import type {
    LambdaFlowEntity,
    LambdaFlowEventHandler,
    LambdaFlowGlobal,
    LambdaFlowMeta,
    LambdaFlowRequestHandler,
    LambdaFlowRequestOptions
} from './lambdaflow';

function sdk(): LambdaFlowGlobal {
    if (!window.LambdaFlow) {
        throw new Error(
            'LambdaFlow JavaScript SDK is not loaded. Load lambdaflow.js before your frontend entrypoint.'
        );
    }

    if (typeof window.send !== 'function') {
        throw new Error(
            'window.send is not available. Run this app inside the LambdaFlow host.'
        );
    }

    return window.LambdaFlow;
}

export function ensureLambdaFlow(): LambdaFlowGlobal {
    return sdk();
}

export function isLambdaFlowAvailable(): boolean {
    return Boolean(window.LambdaFlow && typeof window.send === 'function');
}

export function configureLambdaFlow(
    options: Parameters<LambdaFlowGlobal['configure']>[0]
): LambdaFlowGlobal {
    return sdk().configure(options);
}

export function request<TResult = unknown>(
    kind: string,
    payload: unknown = null,
    timeoutOrOptions: LambdaFlowRequestOptions = 30000
): Promise<TResult> {
    return sdk().request<TResult>(kind, payload, timeoutOrOptions);
}

export function requestEntity<TResult = unknown>(
    kind: string,
    type: string,
    data: unknown,
    timeoutOrOptions: LambdaFlowRequestOptions = 30000,
    version = 1
): Promise<TResult> {
    return sdk().requestEntity<TResult>(kind, type, data, timeoutOrOptions, version);
}

export function send(
    kind: string,
    payload: unknown = null,
    options?: { id?: string; ok?: boolean }
): void {
    sdk().send(kind, payload, options);
}

export function sendEntity(
    kind: string,
    type: string,
    data: unknown,
    version = 1,
    options?: { id?: string; ok?: boolean }
): void {
    sdk().sendEntity(kind, type, data, version, options);
}

export function on<TPayload = unknown>(
    kind: string,
    handler: LambdaFlowEventHandler<TPayload>,
    options?: { once?: boolean; unwrap?: boolean }
): () => void {
    return sdk().on(kind, handler, options);
}

export function onAny(
    handler: LambdaFlowEventHandler,
    options?: { once?: boolean; unwrap?: boolean }
): () => void {
    return sdk().onAny(handler, options);
}

export function once<TPayload = unknown>(
    kind: string,
    handler: LambdaFlowEventHandler<TPayload>,
    options?: { unwrap?: boolean }
): () => void {
    return sdk().once(kind, handler, options);
}

export function handle<TPayload = unknown, TResult = unknown>(
    kind: string,
    handler: LambdaFlowRequestHandler<TPayload, TResult>,
    options?: { unwrap?: boolean }
): () => void {
    return sdk().handle(kind, handler, options);
}

export function pendingCount(): number {
    return sdk().pendingCount();
}

export function entity<T = unknown>(
    type: string,
    data: T,
    version = 1
): LambdaFlowEntity<T> {
    return sdk().entity(type, data, version);
}

export function unwrapEntity<T = unknown>(payload: unknown): T {
    return sdk().unwrapEntity<T>(payload);
}

export const lf = {
    ensureAvailable: ensureLambdaFlow,
    isAvailable: isLambdaFlowAvailable,
    configure: configureLambdaFlow,
    request,
    requestEntity,
    send,
    sendEntity,
    emit: send,
    on,
    onAny,
    once,
    handle,
    pendingCount,
    entity,
    unwrapEntity
};

export type {
    LambdaFlowEntity,
    LambdaFlowEventHandler,
    LambdaFlowGlobal,
    LambdaFlowMeta,
    LambdaFlowRequestHandler,
    LambdaFlowRequestOptions
};
