# Copyright (c) Files Community
# Licensed under the MIT License.

"""Reports the bundled interpreter. Reads no files and writes nothing outside its own output."""

import os
import sys

import tracer_protocol as protocol


def main() -> None:
    protocol.progress(0, "Inspecting the bundled Automation runtime.")
    request = protocol.read_request()

    version = ".".join(str(part) for part in sys.version_info[:3])
    protocol.log("info", f"Interpreter {version} at {sys.executable}")

    # -I gives an isolated interpreter: no user site directory and no PYTHON* environment influence.
    protocol.log("info", f"Isolated: {sys.flags.isolated == 1}, no user site: {sys.flags.no_user_site == 1}")
    protocol.log("info", f"Bytecode writing disabled: {sys.dont_write_bytecode}")

    action = request.get("action", {})
    protocol.log("info", f"Package {action.get('packageId', '?')} version {action.get('packageVersion', '?')}")
    protocol.log("info", f"Working directory {os.getcwd()}")

    protocol.progress(100, "Runtime check complete.")
    protocol.succeeded(f"Bundled CPython {version} is running from the installed app.")


if __name__ == "__main__":
    main()
