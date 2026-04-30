"""
LambdaFlow Python SDK
=====================

Single-file SDK for writing a LambdaFlow backend in Python.

Expected wire format, one JSON object per line:

    {
        "kind": "routing-key",
        "id": "optional-correlation-id",
        "ok": true,
        "payload": {},
        "error": {
            "code": "ERROR_CODE",
            "message": "Human readable message",
            "details": {}
        }
    }

Entity payload format:

    {
        "$type": "animals.dog",
        "$v": 1,
        "data": {
            "name": "Rex"
        }
    }

Transport is auto-detected:

    LAMBDAFLOW_IPC_TRANSPORT=NamedPipe
    LAMBDAFLOW_PIPE_NAME=<pipe-name>

Otherwise stdin/stdout is used.
"""

from __future__ import annotations

import inspect
import json
import logging
import os
import sys
import threading
import time
import uuid
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from typing import Any, Callable, Optional


__all__ = [
    "LambdaFlowError",
    "Meta",
    "configure",
    "receive",
    "on",
    "handle",
    "unhandle",
    "send",
    "emit",
    "request",
    "respond",
    "reject",
    "entity",
    "is_entity",
    "unwrap_entity",
    "send_entity",
    "request_entity",
    "run",
    "stop",
    "pending_count",
]


# ---------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------


DEFAULT_TIMEOUT_MS = 30_000
DEFAULT_MAX_WORKERS = 16
RESULT_KIND_SUFFIX = ".result"


_config = {
    "timeout_ms": DEFAULT_TIMEOUT_MS,
    "unwrap_entities": True,
    "warn_on_unhandled": True,
    "reply_to_events": False,
}


_logger = logging.getLogger("lambdaflow")
if not _logger.handlers:
    handler = logging.StreamHandler(sys.stderr)
    handler.setFormatter(logging.Formatter("[LambdaFlow] %(levelname)s: %(message)s"))
    _logger.addHandler(handler)
_logger.setLevel(logging.INFO)


# ---------------------------------------------------------------------
# Internal state
# ---------------------------------------------------------------------


_handlers: dict[str, Callable[..., Any]] = {}
_pending: dict[str, "_PendingRequest"] = {}

_transport: Optional["_Transport"] = None
_transport_lock = threading.Lock()
_write_lock = threading.Lock()
_pending_lock = threading.Lock()
_stop_event = threading.Event()
_executor: Optional[ThreadPoolExecutor] = None


# ---------------------------------------------------------------------
# Data structures
# ---------------------------------------------------------------------


class LambdaFlowError(Exception):
    def __init__(
        self,
        message: str,
        code: str = "LAMBDAFLOW_ERROR",
        details: Any = None,
        envelope: Optional[dict[str, Any]] = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.code = code
        self.details = details
        self.envelope = envelope

    def to_error_object(self) -> dict[str, Any]:
        error = {
            "code": self.code,
            "message": self.message,
        }

        if self.details is not None:
            error["details"] = self.details

        return error


@dataclass(frozen=True)
class Meta:
    kind: str
    id: Optional[str]
    ok: Optional[bool]
    raw_payload: Any
    is_entity: bool
    type_name: Optional[str]
    version: Optional[int]
    envelope: dict[str, Any]
    received_at: float


@dataclass
class _PendingRequest:
    event: threading.Event
    kind: str
    unwrap: bool
    response: Any = None
    error: Optional[BaseException] = None


# ---------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------


def configure(
    *,
    timeout_ms: Optional[int] = None,
    unwrap_entities: Optional[bool] = None,
    warn_on_unhandled: Optional[bool] = None,
    reply_to_events: Optional[bool] = None,
    log_level: Optional[int] = None,
) -> None:
    """
    Configure SDK behavior.

    Args:
        timeout_ms:
            Default timeout for request(), in milliseconds.

        unwrap_entities:
            If True, handlers receive entity.data instead of the full
            {"$type", "$v", "data"} object. Metadata remains available
            through the optional second handler argument.

        warn_on_unhandled:
            Log when a message arrives with no registered handler.

        reply_to_events:
            Backwards-compatibility mode. If True, a handler return value
            for a fire-and-forget event is sent back as an event.
            Recommended value: False.

        log_level:
            Python logging level, for example logging.DEBUG.
    """
    if timeout_ms is not None:
        if timeout_ms <= 0:
            raise ValueError("timeout_ms must be > 0.")
        _config["timeout_ms"] = timeout_ms

    if unwrap_entities is not None:
        _config["unwrap_entities"] = bool(unwrap_entities)

    if warn_on_unhandled is not None:
        _config["warn_on_unhandled"] = bool(warn_on_unhandled)

    if reply_to_events is not None:
        _config["reply_to_events"] = bool(reply_to_events)

    if log_level is not None:
        _logger.setLevel(log_level)


def receive(kind: str, handler: Optional[Callable[..., Any]] = None):
    """
    Register a handler for frontend -> backend messages.

    The handler may accept either:

        def handler(payload): ...

    or:

        def handler(payload, meta): ...

    If the inbound message has an id, the return value is sent as a response.
    If the handler raises, an ok:false error response is sent.
    """
    _assert_kind(kind)

    def decorator(func: Callable[..., Any]) -> Callable[..., Any]:
        if not callable(func):
            raise TypeError("Handler must be callable.")

        _handlers[kind] = func
        return func

    if handler is not None:
        return decorator(handler)

    return decorator


def on(kind: str, handler: Optional[Callable[..., Any]] = None):
    """
    Alias for receive().
    """
    return receive(kind, handler)


def handle(kind: str, handler: Optional[Callable[..., Any]] = None):
    """
    Alias for receive(), useful when thinking in request/response terms.
    """
    return receive(kind, handler)


def unhandle(kind: str) -> None:
    """
    Remove the handler for a kind.
    """
    _handlers.pop(kind, None)


def send(kind: str, payload: Any = None, *, id: Optional[str] = None, ok: Optional[bool] = None) -> None:
    """
    Send a fire-and-forget event to the frontend.
    """
    _assert_kind(kind)

    envelope: dict[str, Any] = {
        "kind": kind,
        "payload": payload,
    }

    if id is not None:
        envelope["id"] = id

    if ok is not None:
        envelope["ok"] = bool(ok)

    _write_envelope(envelope)


def emit(kind: str, payload: Any = None) -> None:
    """
    Alias for send().
    """
    send(kind, payload)


def request(
    kind: str,
    payload: Any = None,
    *,
    timeout_ms: Optional[int] = None,
    unwrap: Optional[bool] = None,
) -> Any:
    """
    Send a backend -> frontend request and wait for a response.

    This pairs with the JavaScript SDK's:

        LambdaFlow.handle("some.kind", handler)

    Example:

        theme = lf.request("ui.getTheme")
    """
    _assert_kind(kind)

    request_id = str(uuid.uuid4())
    effective_timeout_ms = timeout_ms or int(_config["timeout_ms"])
    effective_unwrap = _config["unwrap_entities"] if unwrap is None else bool(unwrap)

    pending = _PendingRequest(
        event=threading.Event(),
        kind=kind,
        unwrap=effective_unwrap,
    )

    with _pending_lock:
        _pending[request_id] = pending

    try:
        _write_envelope({
            "kind": kind,
            "id": request_id,
            "payload": payload,
        })

        completed = pending.event.wait(effective_timeout_ms / 1000)

        if not completed:
            raise LambdaFlowError(
                f'Request "{kind}" timed out.',
                code="REQUEST_TIMEOUT",
                details={
                    "kind": kind,
                    "id": request_id,
                    "timeout_ms": effective_timeout_ms,
                },
            )

        if pending.error is not None:
            raise pending.error

        return pending.response

    finally:
        with _pending_lock:
            _pending.pop(request_id, None)


def respond(kind: str, id: str, payload: Any = None) -> None:
    """
    Manually send a successful response.
    Usually you do not need this; returning from a handler is enough.
    """
    _assert_kind(kind)

    if not id:
        raise ValueError("respond() requires an id.")

    _write_envelope({
        "kind": kind,
        "id": id,
        "ok": True,
        "payload": payload,
    })


def reject(kind: str, id: str, error: Any) -> None:
    """
    Manually send an error response.
    """
    _assert_kind(kind)

    if not id:
        raise ValueError("reject() requires an id.")

    _write_envelope({
        "kind": kind,
        "id": id,
        "ok": False,
        "error": _to_error_object(error),
    })


def entity(type_name: str, data: Any, version: int = 1) -> dict[str, Any]:
    """
    Build a LambdaFlow entity payload.

    Example:

        lf.entity("animals.dog", {"name": "Rex"})
    """
    if not isinstance(type_name, str) or not type_name.strip():
        raise ValueError("Entity type must be a non-empty string.")

    if not isinstance(version, int) or version < 1:
        raise ValueError("Entity version must be an integer >= 1.")

    return {
        "$type": type_name,
        "$v": version,
        "data": data,
    }


def is_entity(payload: Any) -> bool:
    return (
        isinstance(payload, dict)
        and isinstance(payload.get("$type"), str)
        and "data" in payload
    )


def unwrap_entity(payload: Any) -> Any:
    return payload.get("data") if is_entity(payload) else payload


def send_entity(kind: str, type_name: str, data: Any, version: int = 1) -> None:
    send(kind, entity(type_name, data, version))


def request_entity(
    kind: str,
    type_name: str,
    data: Any,
    *,
    version: int = 1,
    timeout_ms: Optional[int] = None,
    unwrap: Optional[bool] = None,
) -> Any:
    return request(
        kind,
        entity(type_name, data, version),
        timeout_ms=timeout_ms,
        unwrap=unwrap,
    )


def run(
    *,
    threaded: bool = True,
    max_workers: int = DEFAULT_MAX_WORKERS,
    transport: Optional["_Transport"] = None,
) -> None:
    """
    Run the message loop.

    Blocks until the transport closes or stop() is called.
    """
    global _transport, _executor

    _stop_event.clear()

    with _transport_lock:
        if _transport is not None:
            raise RuntimeError("LambdaFlow is already running.")

        _transport = transport or _open_transport()

    effective_threaded = threaded and not isinstance(_transport, _PipeTransport)

    if effective_threaded:
        _executor = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="lambdaflow")
    else:
        _executor = None

    try:
        while not _stop_event.is_set():
            line = _transport.readline()

            if line is None:
                break

            line = line.strip()

            if not line:
                continue

            if _executor is not None:
                _executor.submit(_process_line, line)
            else:
                _process_line(line)

    finally:
        if _executor is not None:
            _executor.shutdown(wait=False, cancel_futures=True)
            _executor = None

        with _transport_lock:
            try:
                if _transport is not None:
                    _transport.close()
            finally:
                _transport = None


def stop() -> None:
    """
    Signal run() to exit.

    Note: if the process is blocked on stdin.readline(), it may only exit
    when stdin closes or a new line is received.
    """
    _stop_event.set()


def pending_count() -> int:
    with _pending_lock:
        return len(_pending)


# ---------------------------------------------------------------------
# Message processing
# ---------------------------------------------------------------------


def _process_line(line: str) -> None:
    try:
        envelope = json.loads(line)
    except json.JSONDecodeError:
        _logger.warning("Received non-JSON line.")
        return

    if not _is_valid_envelope(envelope):
        _logger.warning("Received invalid envelope: %r", envelope)
        return

    _process_envelope(envelope)


def _process_envelope(envelope: dict[str, Any]) -> None:
    msg_id = envelope.get("id")

    if msg_id is not None and _settle_pending_if_response(str(msg_id), envelope):
        return

    kind = envelope["kind"]
    handler = _handlers.get(kind)

    if handler is None:
        if _config["warn_on_unhandled"]:
            _logger.warning("Unhandled message kind: %s", kind)

        if msg_id is not None:
            _write_envelope({
                "kind": _result_kind(kind),
                "id": msg_id,
                "ok": False,
                "error": {
                    "code": "UNHANDLED_KIND",
                    "message": f'No Python handler registered for kind "{kind}".',
                    "details": {
                        "kind": kind,
                    },
                },
            })

        return

    meta = _build_meta(envelope)
    payload = _payload_for_handler(envelope)

    try:
        result = _call_handler(handler, payload, meta)
    except Exception as exc:
        _logger.exception('Handler "%s" threw.', kind)

        if msg_id is not None:
            _write_envelope({
                "kind": _result_kind(kind),
                "id": msg_id,
                "ok": False,
                "error": _to_error_object(exc),
            })

        return

    if msg_id is not None:
        _write_envelope({
            "kind": _result_kind(kind),
            "id": msg_id,
            "ok": True,
            "payload": result,
        })
        return

    if _config["reply_to_events"] and result is not None:
        _write_envelope({
            "kind": kind,
            "ok": True,
            "payload": result,
        })


def _settle_pending_if_response(msg_id: str, envelope: dict[str, Any]) -> bool:
    with _pending_lock:
        pending = _pending.get(msg_id)

    if pending is None:
        return False

    if envelope.get("ok") is False or envelope.get("error") is not None:
        pending.error = _error_from_envelope(envelope)
    elif isinstance(envelope.get("payload"), dict) and "error" in envelope["payload"]:
        # Backwards compatibility with the early SDK format:
        # { payload: { error: "..." } }
        pending.error = _error_from_envelope(envelope)
    else:
        pending.response = _payload_for_consumer(envelope, unwrap=pending.unwrap)

    pending.event.set()
    return True


def _build_meta(envelope: dict[str, Any]) -> Meta:
    payload = envelope.get("payload")
    entity_payload = is_entity(payload)

    return Meta(
        kind=envelope["kind"],
        id=envelope.get("id"),
        ok=envelope.get("ok"),
        raw_payload=payload,
        is_entity=entity_payload,
        type_name=payload.get("$type") if entity_payload else None,
        version=payload.get("$v") if entity_payload else None,
        envelope=envelope,
        received_at=time.time(),
    )


def _payload_for_handler(envelope: dict[str, Any]) -> Any:
    return _payload_for_consumer(
        envelope,
        unwrap=bool(_config["unwrap_entities"]),
    )


def _payload_for_consumer(envelope: dict[str, Any], *, unwrap: bool) -> Any:
    payload = envelope.get("payload")
    return unwrap_entity(payload) if unwrap else payload


def _call_handler(handler: Callable[..., Any], payload: Any, meta: Meta) -> Any:
    """
    Supports both:

        def handler(payload): ...

    and:

        def handler(payload, meta): ...
    """
    try:
        signature = inspect.signature(handler)
    except (TypeError, ValueError):
        return handler(payload)

    parameters = list(signature.parameters.values())

    accepts_varargs = any(p.kind == inspect.Parameter.VAR_POSITIONAL for p in parameters)

    positional = [
        p for p in parameters
        if p.kind in (
            inspect.Parameter.POSITIONAL_ONLY,
            inspect.Parameter.POSITIONAL_OR_KEYWORD,
        )
    ]

    if accepts_varargs or len(positional) >= 2:
        return handler(payload, meta)

    return handler(payload)


# ---------------------------------------------------------------------
# Envelope helpers
# ---------------------------------------------------------------------


def _assert_kind(kind: str) -> None:
    if not isinstance(kind, str) or not kind.strip():
        raise ValueError("kind must be a non-empty string.")


def _result_kind(kind: str) -> str:
    if kind.endswith(RESULT_KIND_SUFFIX):
        return kind
    return f"{kind}{RESULT_KIND_SUFFIX}"


def _is_valid_envelope(value: Any) -> bool:
    return (
        isinstance(value, dict)
        and isinstance(value.get("kind"), str)
        and bool(value["kind"].strip())
    )


def _to_error_object(error: Any) -> dict[str, Any]:
    if isinstance(error, LambdaFlowError):
        return error.to_error_object()

    if isinstance(error, Exception):
        return {
            "code": error.__class__.__name__,
            "message": str(error) or error.__class__.__name__,
        }

    if isinstance(error, str):
        return {
            "code": "ERROR",
            "message": error,
        }

    return {
        "code": "ERROR",
        "message": "Unknown error",
        "details": error,
    }


def _error_from_envelope(envelope: dict[str, Any]) -> LambdaFlowError:
    error = envelope.get("error")

    if error is None and isinstance(envelope.get("payload"), dict):
        error = envelope["payload"].get("error")

    if isinstance(error, str):
        return LambdaFlowError(
            error,
            code="REMOTE_ERROR",
            envelope=envelope,
        )

    if isinstance(error, dict):
        return LambdaFlowError(
            error.get("message") or "Remote error",
            code=error.get("code") or "REMOTE_ERROR",
            details=error.get("details"),
            envelope=envelope,
        )

    return LambdaFlowError(
        "Remote error",
        code="REMOTE_ERROR",
        envelope=envelope,
    )


def _write_envelope(envelope: dict[str, Any]) -> None:
    if not _is_valid_envelope(envelope):
        raise LambdaFlowError(
            "Invalid LambdaFlow envelope.",
            code="INVALID_ENVELOPE",
            details=envelope,
        )

    with _transport_lock:
        transport = _transport

    if transport is None:
        raise LambdaFlowError(
            "LambdaFlow transport is not open. Did you call lf.run()?",
            code="TRANSPORT_NOT_OPEN",
        )

    try:
        line = json.dumps(envelope, ensure_ascii=False, separators=(",", ":"))
    except TypeError as exc:
        raise LambdaFlowError(
            "Payload is not JSON-serializable.",
            code="SERIALIZATION_FAILED",
            details=str(exc),
        ) from exc

    with _write_lock:
        transport.write_line(line)


# ---------------------------------------------------------------------
# Transports
# ---------------------------------------------------------------------


class _Transport:
    def readline(self) -> Optional[str]:
        raise NotImplementedError

    def write_line(self, line: str) -> None:
        raise NotImplementedError

    def close(self) -> None:
        raise NotImplementedError


class _StdioTransport(_Transport):
    def readline(self) -> Optional[str]:
        line = sys.stdin.readline()
        return line if line else None

    def write_line(self, line: str) -> None:
        sys.stdout.write(line)
        sys.stdout.write("\n")
        sys.stdout.flush()

    def close(self) -> None:
        pass


class _PipeTransport(_Transport):
    def __init__(self, name: str, timeout: float = 10.0) -> None:
        path = rf"\\.\pipe\{name}"
        deadline = time.time() + timeout
        last_error: Optional[OSError] = None

        while time.time() < deadline:
            try:
                self._stream = open(path, "r+b", buffering=0)
                break
            except OSError as exc:
                last_error = exc
                time.sleep(0.1)
        else:
            if last_error is not None:
                raise last_error
            raise OSError(f"Could not open pipe {path}")

    def readline(self) -> Optional[str]:
        chunks: list[bytes] = []

        while True:
            byte = self._stream.read(1)

            if not byte:
                if not chunks:
                    return None

                return b"".join(chunks).decode("utf-8").rstrip("\r")

            if byte == b"\n":
                return b"".join(chunks).decode("utf-8").rstrip("\r")

            chunks.append(byte)

    def write_line(self, line: str) -> None:
        self._stream.write(line.encode("utf-8"))
        self._stream.write(b"\n")
        self._stream.flush()

    def close(self) -> None:
        try:
            self._stream.close()
        except Exception:
            pass


def _open_transport() -> _Transport:
    transport = os.environ.get("LAMBDAFLOW_IPC_TRANSPORT", "")

    if transport.lower() == "namedpipe":
        pipe_name = os.environ.get("LAMBDAFLOW_PIPE_NAME")

        if not pipe_name:
            raise RuntimeError("LAMBDAFLOW_PIPE_NAME is required for NamedPipe transport.")

        return _PipeTransport(pipe_name)

    return _StdioTransport()
