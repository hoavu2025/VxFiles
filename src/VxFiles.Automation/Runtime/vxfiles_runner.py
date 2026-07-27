# Copyright (c) Files Community
# Licensed under the MIT License.

import importlib.util
import ctypes
import os
import runpy
import sys


def _load_helper() -> None:
    helper_path = os.path.join(os.path.dirname(__file__), "vxfiles_automation.py")
    spec = importlib.util.spec_from_file_location("vxfiles_automation", helper_path)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the bundled Automation helper.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    sys.modules["vxfiles_automation"] = module


def main() -> None:
    # Block until VxFiles has assigned this process to its kill-on-close Job Object.
    start_event_name = os.environ["VXFILES_AUTOMATION_START_EVENT"]
    start_event = ctypes.windll.kernel32.OpenEventW(0x00100000, False, start_event_name)
    if not start_event:
        raise OSError("Could not open the Automation Job assignment gate.")
    try:
        ctypes.windll.kernel32.WaitForSingleObject(start_event, 0xFFFFFFFF)
    finally:
        ctypes.windll.kernel32.CloseHandle(start_event)
    if len(sys.argv) < 3:
        raise RuntimeError("The Automation runner requires an entry point and package root.")
    entry_point = os.path.abspath(sys.argv[1])
    package_root = os.path.abspath(sys.argv[2])
    action_arguments = sys.argv[3:]
    library_path = os.path.join(package_root, "lib")
    retained_runtime_paths = [path for path in sys.path if path]
    sys.path[:] = [os.path.dirname(entry_point), library_path, *retained_runtime_paths]
    os.chdir(package_root)
    _load_helper()
    sys.argv = [entry_point, *action_arguments]
    runpy.run_path(entry_point, run_name="__main__")


if __name__ == "__main__":
    main()
