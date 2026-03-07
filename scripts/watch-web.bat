@echo off
echo Building Game.Web (initial build)...
dotnet build Game.Web/Game.Web.csproj
if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Starting server at http://localhost:8080
start "Game.Web Server" cmd /c "dotnet serve -p 8080 -d Game.Web\bin\Debug\net10.0\browser-wasm\AppBundle --mime .wasm=application/wasm --mime .js=application/javascript"

echo.
echo Watching for changes... (Ctrl+C to stop)
pushd Game.Web
dotnet watch build
popd
