#!/usr/bin/env bash
set -e

echo "Building and running Game.Server..."
dotnet run --project Game.Server/Game.Server.csproj -- "$@"
