# Copyright (c) Files Community
# Licensed under the MIT License.

import json
import os
import sys


request = json.load(sys.stdin)
output_path = os.path.join(request["activeFolderPath"], "vxfiles-selection.txt")
with open(output_path, "w", encoding="utf-8", newline="\n") as output:
    for item in request["items"]:
        output.write(item["path"] + "\n")

print(json.dumps({
    "protocol": "ndjson-v1",
    "sequence": 1,
    "type": "result",
    "outcome": "succeeded",
    "message": "Selection list written",
    "effects": [
        {"type": "refreshCurrentFolder"},
        {"type": "revealPaths", "paths": [output_path]},
    ],
}), flush=True)
