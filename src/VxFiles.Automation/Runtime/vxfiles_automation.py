# Copyright (c) Files Community
# Licensed under the MIT License.

import ctypes
import os


_SYNCHRONIZE = 0x00100000
_WAIT_OBJECT_0 = 0


def cancellation_requested() -> bool:
    """Returns True once VxFiles has asked this action to stop cooperatively."""
    event_name = os.environ.get("VXFILES_AUTOMATION_CANCEL_EVENT")
    if not event_name:
        return False
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    handle = kernel32.OpenEventW(_SYNCHRONIZE, False, event_name)
    if not handle:
        return False
    try:
        return kernel32.WaitForSingleObject(handle, 0) == _WAIT_OBJECT_0
    finally:
        kernel32.CloseHandle(handle)
