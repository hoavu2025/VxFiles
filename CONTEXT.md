# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and designed for controlled sharing with coworkers.

## Language

**VxFiles**:
The user-facing identity of the personalized application and its distributions. Internal source identifiers inherited from Files Community are not part of the VxFiles brand.
_Avoid_: Files - Dev, Files - VxDev

**Portable Build**:
A self-contained, unpackaged ZIP distribution that runs without installation or administrator rights. Application settings may remain in the current user's profile rather than traveling with the application folder.
_Avoid_: Portable installer, standalone EXE

**Zero-Integration Default**:
The Portable Build makes no automatic, persistent changes to Windows integration. Optional per-user integrations must be initiated explicitly and must not require administrator rights.
_Avoid_: Silent registration, automatic shell integration

**Local Diagnostics**:
Diagnostic information retained on the user's device for troubleshooting. VxFiles does not transmit telemetry or crash reports.
_Avoid_: Analytics, remote crash reporting

**VxFiles Data**:
Settings, caches, diagnostics, and other persistent state owned exclusively by VxFiles under the current user's local application data. It neither shares with nor migrates data from an installed Files application.
_Avoid_: Files data, portable settings
