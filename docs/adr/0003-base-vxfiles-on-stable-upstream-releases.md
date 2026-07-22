# Base VxFiles on stable upstream releases

Start VxFiles from Files v4.2 and incorporate future upstream changes from stable release tags rather than continuously following `upstream/main`. `origin/main` holds stable VxFiles code; each upstream release is merged with a merge commit through a temporary `sync/files-vX.Y` branch only after the Portable Build and smoke tests pass. This provides reproducible portable baselines and traceable upstream history, at the cost of receiving unreleased changes later.
