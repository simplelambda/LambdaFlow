"""
LambdaFlow Python SDK
=====================

Drop-in single-file SDK for writing a LambdaFlow backend in Python.

Quick start
-----------

    import lambdaflow as lf

    @lf.receive("uppercase")
    def upper(payload):
        return {"text": payload["text"].upper()}

    @lf.receive("describeDog")
    def describe(dog):
        return f"{dog['name']} is a {dog['age']}-year-old {dog['breed']}"

    lf.run()

Wire format (one JSON object per line)::

    { "kind": "<routing-key>", "id": "<uuid|null>", "payload": <any-json|null> }

Transport is auto-detected: NamedPipe when ``LAMBDAFLOW_IPC_TRANSPORT=NamedPipe``
(with ``LAMBDAFLOW_PIPE_NAME``), otherwise stdin/stdout.
"""

from __future__ import annotations

import json
import os
import sys
import threading
import time
from typing import Any, Callable, Optional

__all__ = ["receive", "on", "send", "entity", "send_entity", "run", "stop"]

_handlers: dict[str, Callable[[Any], Any]] = {}
_write_lock = threading.Lock()
_transport: Optional["_Transport"] = None
_stop_event = threading.Event()


# -- Public API ----------------------------------------------------------


def receive(kind: str, handler: Optional[Callable[[Any], Any]] = None):
    """Register a handler for messages with the given ``kind``.

    The handler receives the deserialized JSON payload and may return any
    JSON-serializable value. If the inbound message carried an ``id`` (i.e.,
    it was a request), the return value is sent back as the response. For
    fire-and-forget events, the return value is forwarded to the frontend
    only if not ``None``.

    Used as a decorator::

        @lf.receive("ping")
        def ping(_):
            return "pong"

    Or directly::

        lf.receive("ping", lambda _: "pong")
    """
    def decorator(func: Callable[[Any], Any]) -> Callable[[Any], Any]:
        _handlers[kind] = func
        return func

    if handler is not None:
        return decorator(handler)
    return decorator


def on(kind: str) -> Callable[[Callable[[Any], Any]], Callable[[Any], Any]]:
    """Backward-compatible alias for :func:`receive`."""
    return receive(kind)


def send(kind: str, payload: Any = None) -> None:
    """Send an event to the frontend (no response expected)."""
    _write_envelope({"kind": kind, "payload": payload})


def entity(type_name: str, data: Any, version: int = 1) -> dict[str, Any]:
    """Build an ontology payload: ``{"$type": "...", "$v": 1, "data": {...}}``."""
    if not type_name:
        raise ValueError("Entity type must be non-empty.")
    if version < 1:
        raise ValueError("Entity version must be >= 1.")
    return {"$type": type_name, "$v": version, "data": data}


def send_entity(kind: str, type_name: str, data: Any, version: int = 1) -> None:
    """Send an ontology entity payload to the frontend."""
    send(kind, entity(type_name, data, version))


def run() -> None:
    """Run the message loop. Blocks until the transport closes or :func:`stop` is called."""
    global _transport
    _transport = _open_transport()

    try:
        while not _stop_event.is_set():
            line = _transport.readline()
            if line is None:
                break
            line = line.strip()
            if not line:
                continue
            threading.Thread(target=_process_line, args=(line,), daemon=True).start()
    finally:
        try:
            _transport.close()
        except Exception:
            pass


def stop() -> None:
    """Signal :func:`run` to exit."""
    _stop_event.set()


# -- Internals -----------------------------------------------------------


def _process_line(line: str) -> None:
    try:
        envelope = json.loads(line)
    except json.JSONDecodeError:
        return

    kind = envelope.get("kind")
    msg_id = envelope.get("id")
    payload = _unwrap_ontology_payload(envelope.get("payload"))

    if not kind or kind not in _handlers:
        return

    try:
        result = _handlers[kind](payload)
    except Exception as exc:
        print(f"[LambdaFlow] handler '{kind}' threw: {exc}", file=sys.stderr)
        if msg_id is not None:
            _write_envelope({"kind": kind, "id": msg_id, "payload": {"error": str(exc)}})
        return

    if msg_id is not None:
        _write_envelope({"kind": kind, "id": msg_id, "payload": result})
    elif result is not None:
        _write_envelope({"kind": kind, "payload": result})


def _write_envelope(envelope: dict) -> None:
    if _transport is None:
        return
    line = json.dumps(envelope, ensure_ascii=False, separators=(",", ":"))
    with _write_lock:
        _transport.write_line(line)


def _unwrap_ontology_payload(payload: Any) -> Any:
    if not isinstance(payload, dict):
        return payload
    if "$type" not in payload or "data" not in payload:
        return payload
    return payload.get("data")


# -- Transports ----------------------------------------------------------


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
        last_err: Optional[OSError] = None
        while time.time() < deadline:
            try:
                self._stream = open(path, "r+b", buffering=0)
                break
            except OSError as exc:
                last_err = exc
                time.sleep(0.1)
        else:
            raise last_err if last_err else OSError(f"Could not open pipe {path}")

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
            raise RuntimeError("LAMBDAFLOW_PIPE_NAME is required for named pipe transport.")
        return _PipeTransport(pipe_name)
    return _StdioTransport()
