@echo off
echo Publishing Game.Web (Release)...
dotnet publish Game.Web/Game.Web.csproj -c Release
if errorlevel 1 (
    echo Publish failed!
    pause
    exit /b 1
)
echo.
echo Starting server at http://localhost:8080
pushd Game.Web\bin\Release\net10.0\browser-wasm\AppBundle
dotnet serve -p 8080 --mime .wasm=application/wasm --mime .js=application/javascript
popd
