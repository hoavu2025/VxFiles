# Isolate VxFiles data without registry storage

## Context and Decision

Store all VxFiles application state under `%LOCALAPPDATA%\VxFiles` without reading, creating, or modifying Windows Registry keys or Files Community application data.

## File Tag Persistence and ADS Supplemental Model

- **Authoritative Persistence**: File tags are stored locally in `%LOCALAPPDATA%\VxFiles\filetags.db` using JSON persistence with atomic file writes and inter-process/thread mutex synchronization (`Local\VxFiles-FileTags`).
- **File Reference Number (FRN)**: Records match items by FRN and path to preserve tag assignments across item renames and moves, preventing duplicate database entries.
- **ADS Supplemental Storage**: On supported NTFS volumes, tag markers are attached via Alternate Data Streams (`:files` ADS). On non-NTFS volumes or filesystems that do not support ADS (e.g. FAT32, exFAT, network SMB shares), ADS write failures are handled gracefully as a non-fatal supplemental step; the authoritative JSON database continues to maintain file tag associations without blocking tag operations.

## System Tray and Background Lifecycle

- System tray notification icon (`ShowSystemTrayIcon`) and background execution mode (`LeaveAppRunning`) operate strictly as optional runtime behaviors and default to `false` (OFF) for both ZIP portable and Inno Setup builds.
- Neither option writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` or registers OS startup tasks/shell extensions, preserving zero-install portable deployment integrity.
