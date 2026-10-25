#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
VERSION="$(/usr/bin/python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$ROOT/macos/plugin.json")"
BUILD="$ROOT/macos-native"
STAGE="$ROOT/artifacts/plugin-$VERSION-macos"
rm -rf "$BUILD" "$STAGE"
mkdir -p "$BUILD" "$STAGE/native" "$STAGE/assets/Skins" "$STAGE/quotes" "$STAGE/docs"

SOURCES=("$ROOT"/macos-src/*.swift)
SDK="$(xcrun --sdk macosx --show-sdk-path)"
for ARCH in arm64 x86_64; do
  xcrun swiftc "${SOURCES[@]}" -O -sdk "$SDK" -target "$ARCH-apple-macos13.0" -framework AppKit -o "$BUILD/CodexPetPlugin-$ARCH"
done
lipo -create "$BUILD/CodexPetPlugin-arm64" "$BUILD/CodexPetPlugin-x86_64" -output "$BUILD/CodexPetPlugin"
chmod +x "$BUILD/CodexPetPlugin"
codesign --force --sign - "$BUILD/CodexPetPlugin"

cp "$ROOT/macos/plugin.json" "$STAGE/plugin.json"
cp "$ROOT/macos/mcp.json" "$STAGE/mcp.json"
cp "$ROOT/macos/README.md" "$STAGE/README.md"
cp -R "$ROOT/macos/hooks" "$STAGE/hooks"
cp "$ROOT/LICENSE" "$ROOT/LICENSE.whale-widget" "$STAGE/"
cp "$ROOT/assets/DSniang1.png" "$ROOT/assets/DSniang1-no-ahoge.png" "$ROOT/assets/"*.mp3 "$STAGE/assets/"
cp "$ROOT/assets/Skins/"*.png "$STAGE/assets/Skins/"
cp "$ROOT/quotes/"*.txt "$STAGE/quotes/"
cp "$ROOT/docs/"*.md "$STAGE/docs/" 2>/dev/null || true
cp "$BUILD/CodexPetPlugin" "$STAGE/native/CodexPetPlugin"
echo "PLUGIN_STAGE $STAGE"
