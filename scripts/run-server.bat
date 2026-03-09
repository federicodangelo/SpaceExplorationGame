@echo off
echo Building and running Game.Server...
dotnet run  --no-restore --project Game.Server/Game.Server.csproj -- %*
if errorlevel 1 (
    echo Run failed!
    pause
    exit /b 1
)
