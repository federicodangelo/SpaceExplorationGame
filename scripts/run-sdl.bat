@echo off
echo Building and running Game.Sdl...
dotnet run --project Game.Sdl/Game.Sdl.csproj -- %*
if errorlevel 1 (
    echo Run failed!
    pause
    exit /b 1
)
