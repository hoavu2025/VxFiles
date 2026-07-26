# Base VxFiles on stable upstream releases

The clean VxFiles V1 line starts directly from Files `v4.2`.

For each future upstream release, create a dedicated `sync/files-vX.Y` branch from accepted VxFiles `main`, fetch the named stable Files tag, and merge it with an explicit merge commit. Do not follow `upstream/main`, prereleases, or arbitrary commits.

Resolve conflicts by restoring upstream behavior first and then reapplying only downstream concerns that remain necessary. Compare the complete result with the new stable tag, remove redundant downstream hunks, and pass the bounded-diff and build gates before promotion.
