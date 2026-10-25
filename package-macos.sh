#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
"$ROOT/build-macos.sh"
VERSION="$(/usr/bin/python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$ROOT/macos/plugin.json")"
STAGE="$ROOT/artifacts/plugin-$VERSION-macos"
ZIP="$ROOT/artifacts/CodexPet-Plugin-$VERSION-macOS-universal.zip"
rm -f "$ZIP" "$ZIP.sha256"
(cd "$STAGE" && /usr/bin/zip -qry "$ZIP" .)
(cd "$ROOT/artifacts" && shasum -a 256 "$(basename "$ZIP")" > "$(basename "$ZIP").sha256")
echo "PLUGIN_ZIP $ZIP"
