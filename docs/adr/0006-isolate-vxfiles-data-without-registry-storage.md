# Isolate VxFiles data without registry storage

Store VxFiles state under `%LOCALAPPDATA%\VxFiles` without reading or modifying Files Community application data. File tags are stored locally in `%LOCALAPPDATA%\VxFiles\filetags.db` using JSON persistence with atomic file operations and multi-process/thread locks, replacing current-user registry storage without requiring package identity or administrator privileges.
