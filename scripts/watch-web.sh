#!/usr/bin/env bash
set -e

echo "Building Game.Web (initial build)..."
dotnet build Game.Web/Game.Web.csproj

echo ""
echo "Starting server at http://localhost:8080"
dotnet serve -p 8080 -d Game.Web/bin/Debug/net10.0/browser-wasm/AppBundle --mime .wasm=application/wasm --mime .js=application/javascript &
SERVER_PID=$!

echo ""
echo "Watching for changes... (Ctrl+C to stop)"
cd Game.Web
dotnet watch build

kill $SERVER_PID 2>/dev/null
