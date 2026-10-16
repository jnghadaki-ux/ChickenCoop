#!/usr/bin/env bash
# Builds Chicken Coop and the drop-in server package:
#   artifacts/ChickenCoop-<version>.zip  ->  addons/counterstrikesharp/plugins/ChickenCoop/...
#                                            addons/counterstrikesharp/shared/ChickenCoop.Shared/...
# Unzip it into game/csgo/. Requires the .NET 10 SDK.
set -euo pipefail
cd "$(dirname "$0")"
VERSION=$(grep -oP 'ModuleVersion => "\K[^"]+' src/ChickenCoopPlugin.cs)

dotnet test tests/ChickenCoop.Tests.csproj
dotnet build src/ChickenCoop.csproj -c Release

rm -rf artifacts
OUT=artifacts/pkg
DIR="$OUT/addons/counterstrikesharp/plugins/ChickenCoop"
SHARED="$OUT/addons/counterstrikesharp/shared/ChickenCoop.Shared"
BIN=src/bin/Release/net10.0
SBIN=shared/ChickenCoop.Shared/bin/Release/net10.0
mkdir -p "$DIR" "$SHARED"
cp "$BIN/ChickenCoop.dll" "$BIN/ChickenCoop.deps.json" "$DIR/"
[ -f "$BIN/ChickenCoop.pdb" ] && cp "$BIN/ChickenCoop.pdb" "$DIR/"
cp -r "$BIN/lang" "$DIR/"
cp "$SBIN/ChickenCoop.Shared.dll" "$SHARED/"
(cd "$OUT" && python3 -c "import shutil; shutil.make_archive('../ChickenCoop-$VERSION', 'zip', '.', 'addons')")
echo "Package: artifacts/ChickenCoop-$VERSION.zip"
