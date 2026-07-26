# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and designed for controlled sharing with coworkers.

## Language

**VxFiles**:
The user-facing identity of the personalized application and its distributions. Internal source identifiers inherited from Files Community are not part of the VxFiles brand.
_Avoid_: Files - Dev, Files - VxDev

**Installed Distribution**:
The only supported VxFiles distribution: a self-contained MSIX package registered by Windows and delivered through App Installer from the VxFiles fork.
_Avoid_: Portable Build, ZIP build, unpackaged build

**Downstream Layer**:
The intentionally small set of VxFiles-owned differences applied to a tagged Files Community baseline.
_Avoid_: Independent codebase, source fork rewrite

**Automation Action**:
A future named automation that users discover and filter in the third right-pane tab, after Details and Preview.
_Avoid_: Automation Bar, automation session

**VxFiles Data**:
Settings, caches, and other persistent state owned by the VxFiles package identity. V1 neither imports nor preserves data from Files or earlier VxFiles distributions.
_Avoid_: Files data, portable settings
