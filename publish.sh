#!/usr/bin/env bash
# Release build Chaos City - zipy v ./release/
set -e
for R in win-x64 linux-x64 osx-arm64; do
  dotnet publish -c Release -r "$R" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    -o "release/ChaosCity-$R"
  cp README.md "release/ChaosCity-$R/"
  (cd release && zip -rq "ChaosCity-$R.zip" "ChaosCity-$R")
done
echo "Hotovo."
