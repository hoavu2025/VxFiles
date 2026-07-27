# Copyright (c) Files Community
# Licensed under the MIT License.

"""Minimal ndjson-v1 writer shared by the tracer actions.

Living under the package's lib directory also proves the runner puts that directory on sys.path.
"""

import json
import sys

_PROTOCOL = "ndjson-v1"
_sequence = 1


def _emit(frame: dict) -> None:
    global _sequence
    frame["protocol"] = _PROTOCOL
    frame["sequence"] = _sequence
    _sequence += 1
    sys.stdout.write(json.dumps(frame, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def progress(percent: float, message: str) -> None:
    _emit({"type": "progress", "percent": percent, "message": message})


def log(level: str, message: str) -> None:
    _emit({"type": "log", "level": level, "message": message})


def succeeded(message: str) -> None:
    """Emits the terminal frame. Nothing may be written after this."""
    _emit({"type": "result", "outcome": "succeeded", "message": message, "effects": []})


def read_request() -> dict:
    return json.loads(sys.stdin.read() or "{}")
