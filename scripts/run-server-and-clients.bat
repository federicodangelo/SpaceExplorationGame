@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "CLIENT_COUNT=%~1"

if "%CLIENT_COUNT%"=="" set "CLIENT_COUNT=2"

echo(%CLIENT_COUNT%| findstr /r "^[1-9][0-9]*$" >nul
if errorlevel 1 (
	echo Invalid client count: %CLIENT_COUNT%
	echo Usage: %~n0 [client_count]
	exit /b 1
)

echo Starting server...
start "SpaceExplorationGame Server" cmd /c call "%SCRIPT_DIR%run-server.bat"

echo Waiting 2 seconds for server startup...
timeout /t 2 /nobreak >nul

for /l %%I in (1,1,%CLIENT_COUNT%) do (
	echo Starting client %%I...
	start "SpaceExplorationGame Client %%I" cmd /c call "%SCRIPT_DIR%run-client.bat"
)

endlocal