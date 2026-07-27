# Copyright (c) Files Community
# Licensed under the MIT License.

"""Summarizes the captured selection. Reads no file contents and changes nothing on disk."""

import os

import tracer_protocol as protocol

_MAX_LISTED_ITEMS = 20


def main() -> None:
    protocol.progress(0, "Reading the captured selection.")
    request = protocol.read_request()

    items = request.get("items", [])
    protocol.log("info", f"Active folder: {request.get('activeFolderPath', '?')}")
    protocol.log("info", f"Captured at: {request.get('capturedAtUtc', '?')}")

    files = sum(1 for item in items if item.get("kind") == "file")
    folders = len(items) - files
    unc = sum(1 for item in items if item.get("locationKind") == "unc")

    # Only names are reported. Full paths stay out of the log, which is retained in run history.
    # The listing is capped because maxOutputBytes is 64 KiB and a selection may hold thousands of items.
    listed = items[:_MAX_LISTED_ITEMS]
    for index, item in enumerate(listed, start=1):
        protocol.progress(index * 100 / max(len(listed), 1), f"Item {index} of {len(items)}")
        protocol.log("debug", f"{item.get('kind', '?')}: {os.path.basename(item.get('path', ''))}")

    if len(items) > len(listed):
        protocol.log("info", f"{len(items) - len(listed)} further item(s) not listed.")

    protocol.succeeded(f"Selection holds {files} file(s) and {folders} folder(s), {unc} on UNC paths.")


if __name__ == "__main__":
    main()
