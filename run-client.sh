#!/usr/bin/env bash
set -e

echo "Building and running Game.Sdl (multiplayer client)..."
dotnet run --project Game.Sdl/Game.Sdl.csproj -- --connect ws://localhost:9050/ "$@"
