#!/usr/bin/env bash
# Builds Chicken Courier and the drop-in server package:
#   artifacts/ChickenCourier-<version>.zip  ->  addons/counterstrikesharp/plugins/ChickenCourier/...
# Unzip it into game/csgo/. Requires the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(grep -oP 'ModuleVersion => "\K[^"]+' src/CourierPlugin.cs)

dotnet test tests/ChickenCourier.Tests.csproj
dotnet build src/ChickenCourier.csproj -c Release

rm -rf artifacts
OUT=artifacts/pkg
DIR="$OUT/addons/counterstrikesharp/plugins/ChickenCourier"
BIN=src/bin/Release/net10.0
mkdir -p "$DIR"
cp "$BIN/ChickenCourier.dll" "$BIN/ChickenCourier.deps.json" "$DIR/"
[ -f "$BIN/ChickenCourier.pdb" ] && cp "$BIN/ChickenCourier.pdb" "$DIR/"
cp -r "$BIN/lang" "$DIR/"
(cd "$OUT" && python3 -c "import shutil; shutil.make_archive('../ChickenCourier-$VERSION', 'zip', '.', 'addons')")
echo "Package: artifacts/ChickenCourier-$VERSION.zip"
