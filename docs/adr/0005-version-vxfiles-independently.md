# Version VxFiles independently

VxFiles release tags and Velopack package versions use matching `MAJOR.MINOR.PATCH` versions.

The first Velopack release is tag and package version `v2.0.1`. Tag `v2.0.0` is retained as an immutable MSIX prototype and must not be reused. "V1" describes the refactor scope, while version 2 avoids colliding with historical VxFiles tags `v1.0.0` through `v1.0.2`.

Published versions increase monotonically. A published tag, package version, or release asset is immutable and is never replaced or reused. Recovery always uses a higher version.
