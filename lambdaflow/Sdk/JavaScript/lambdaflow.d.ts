export type LambdaFlowRequestOptions =
    | number
    | {
        timeoutMs?: number;
        unwrap?: boolean;
        signal?: AbortSignal;
        id?: string;
    };

export type LambdaFlowEnvelope<TPayload = unknown> = {
    kind: string;
    id?: string;
    ok?: boolean;
    payload?: TPayload;
    error?: {
        code?: string;
        message: string;
        details?: unknown;
    };
};

export type LambdaFlowMeta = {
    kind?: string;
    id?: string;
    ok?: boolean;
    rawPayload?: unknown;
    isEntity?: boolean;
    type?: string;
    version?: number;
    envelope?: LambdaFlowEnvelope;
    receivedAt?: number;
};

export type LambdaFlowEntity<T = unknown> = {
    $type: string;
    $v: number;
    data: T;
};

export type LambdaFlowEventHandler<TPayload = unknown> =
    (payload: TPayload, meta: LambdaFlowMeta) => void;

export type LambdaFlowRequestHandler<TPayload = unknown, TResult = unknown> =
    (payload: TPayload, meta: LambdaFlowMeta) => TResult | Promise<TResult>;

export type LambdaFlowGlobal = {
    version: string;
    Error: typeof Error;

    configure(options: {
        timeoutMs?: number;
        unwrapEntities?: boolean;
        warnOnUnhandled?: boolean;
        logger?: Console | null;
        transportSend?: (raw: string) => void;
    }): LambdaFlowGlobal;

    isHostAvailable(): boolean;
    isAvailable(): boolean;
    ensureHostAvailable(): LambdaFlowGlobal;
    ensureAvailable(): LambdaFlowGlobal;

    request<TResult = unknown>(
        kind: string,
        payload?: unknown,
        timeoutOrOptions?: LambdaFlowRequestOptions
    ): Promise<TResult>;

    requestEntity<TResult = unknown>(
        kind: string,
        type: string,
        data: unknown,
        timeoutOrOptions?: LambdaFlowRequestOptions,
        version?: number
    ): Promise<TResult>;

    send(
        kind: string,
        payload?: unknown,
        options?: { id?: string; ok?: boolean }
    ): LambdaFlowEnvelope;

    emit(
        kind: string,
        payload?: unknown,
        options?: { id?: string; ok?: boolean }
    ): LambdaFlowEnvelope;

    sendEntity(
        kind: string,
        type: string,
        data: unknown,
        version?: number,
        options?: { id?: string; ok?: boolean }
    ): LambdaFlowEnvelope<LambdaFlowEntity>;

    on<TPayload = unknown>(
        kind: string,
        handler: LambdaFlowEventHandler<TPayload>,
        options?: { once?: boolean; unwrap?: boolean }
    ): () => void;

    onAny(
        handler: LambdaFlowEventHandler,
        options?: { once?: boolean; unwrap?: boolean }
    ): () => void;

    receive<TPayload = unknown>(
        kind: string,
        handler: LambdaFlowEventHandler<TPayload>,
        options?: { once?: boolean; unwrap?: boolean }
    ): () => void;

    once<TPayload = unknown>(
        kind: string,
        handler: LambdaFlowEventHandler<TPayload>,
        options?: { unwrap?: boolean }
    ): () => void;

    off(kind: string, handler?: LambdaFlowEventHandler): LambdaFlowGlobal;

    handle<TPayload = unknown, TResult = unknown>(
        kind: string,
        handler: LambdaFlowRequestHandler<TPayload, TResult>,
        options?: { unwrap?: boolean }
    ): () => void;

    unhandle(kind: string): LambdaFlowGlobal;

    respond(kind: string, id: string, payload?: unknown): LambdaFlowEnvelope;
    reject(kind: string, id: string, error: unknown): LambdaFlowEnvelope;

    entity<T = unknown>(type: string, data: T, version?: number): LambdaFlowEntity<T>;
    isEntity(payload: unknown): payload is LambdaFlowEntity;
    unwrapEntity<T = unknown>(payload: unknown): T;
    entityType(payload: unknown): string | undefined;
    entityVersion(payload: unknown): number | undefined;

    sendEnvelope<TPayload = unknown>(envelope: LambdaFlowEnvelope<TPayload>): LambdaFlowEnvelope<TPayload>;
    receiveRaw(raw: string | LambdaFlowEnvelope): void;
    _receiveRaw(raw: string | LambdaFlowEnvelope): void;

    pendingCount(): number;
    clearHandlers(): LambdaFlowGlobal;
    destroy(): void;
};

declare global {
    var LambdaFlow: LambdaFlowGlobal;

    interface Window {
        LambdaFlow?: LambdaFlowGlobal;
        send?: (raw: string) => void;
        receive?: (raw: string | LambdaFlowEnvelope) => void;
        __lambdaFlowInboundQueue?: Array<string | LambdaFlowEnvelope>;
    }
}

declare const LambdaFlow: LambdaFlowGlobal;
export default LambdaFlow;
