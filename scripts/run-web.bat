@echo off
echo Building Game.Web...
dotnet build Game.Web/Game.Web.csproj
if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)
echo.
echo Starting server at http://localhost:8080
pushd Game.Web\bin\Debug\net10.0\browser-wasm\AppBundle
dotnet serve -p 8080 --mime .wasm=application/wasm --mime .js=application/javascript
popd
