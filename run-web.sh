#!/usr/bin/env bash
set -e

echo "Building Game.Web..."
dotnet build Game.Web/Game.Web.csproj

echo ""
echo "Starting server at http://localhost:8080"
cd Game.Web/bin/Debug/net10.0/browser-wasm/AppBundle
dotnet serve -p 8080 --mime .wasm=application/wasm --mime .js=application/javascript
