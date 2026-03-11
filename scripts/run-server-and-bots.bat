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

REM Extract the first argument and the rest of the arguments
for /f "tokens=1*" %%a in ("%*") do (
    REM Store all remaining arguments
    SET TheRest=%%b
)

echo Compiling server and client...
dotnet build --no-restore

echo Starting server...
start "SpaceExplorationGame Server" cmd /k call "%SCRIPT_DIR%run-server.bat" %TheRest%

echo Waiting 2 seconds for server startup...
timeout /t 2 /nobreak >nul

for /l %%I in (1,1,%CLIENT_COUNT%) do (
	echo Starting client %%I...
	start "SpaceExplorationGame Client %%I" cmd /k call "%SCRIPT_DIR%run-client.bat" --name "Player-%%I" --autoplay
)

endlocal