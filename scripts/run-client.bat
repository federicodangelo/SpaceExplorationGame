@echo off
echo Building and running Game.Sdl (multiplayer client)...
dotnet run  --no-restore --project Game.Sdl/Game.Sdl.csproj -- --connect ws://localhost:9050/ %*
if errorlevel 1 (
    echo Run failed!
    pause
    exit /b 1
)
