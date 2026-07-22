# Isolate VxFiles data without registry storage

Store VxFiles state under `%LOCALAPPDATA%\VxFiles` without reading or modifying Files Community application data. Disable file tags in Milestone 1 because upstream stores them in the current-user registry and relies on package identity; tags may return after receiving a registry-free storage implementation.
