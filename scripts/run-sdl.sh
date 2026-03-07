#!/usr/bin/env bash
set -e

echo "Building and running Game.Sdl..."
dotnet run --project Game.Sdl/Game.Sdl.csproj
