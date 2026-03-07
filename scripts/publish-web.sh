#!/usr/bin/env bash
set -e

echo "Publishing Game.Web (Release)..."
dotnet publish Game.Web/Game.Web.csproj -c Release

echo ""
echo "Starting server at http://localhost:8080"
cd Game.Web/bin/Release/net10.0/browser-wasm/AppBundle
dotnet serve -p 8080 -c-1 --mime .wasm=application/wasm --mime .js=application/javascript
