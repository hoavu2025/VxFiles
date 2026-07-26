# Version VxFiles independently

VxFiles release tags use `vMAJOR.MINOR.PATCH`; matching MSIX package versions use `MAJOR.MINOR.PATCH.0`.

The installable line begins at tag `v2.0.0` and package version `2.0.0.0`. “V1” describes the refactor scope, while version 2 avoids colliding with historical VxFiles tags `v1.0.0` through `v1.0.2`.

Published versions increase monotonically. A published tag, package version, or signed asset is immutable and is never replaced or reused. Recovery always uses a higher version.
