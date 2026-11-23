@echo off
REM Roguelike Card Game - dev environment launcher.
REM Run dev.bat (or double-click) to start Server + Client in separate windows.
REM Server uses dotnet watch (auto-rebuild on .cs changes).
REM Client uses Vite hot reload and opens the browser when ready.
REM Re-run dev.bat to restart. It stops earlier launcher windows, the expected
REM ports, and any stale Server.exe built from this repository.

setlocal
set "ROOT=%~dp0"

echo === Stopping existing Roguelike Card Game dev processes ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\stop-dev.ps1" -RootDirectory "%ROOT%."
if errorlevel 1 (
    echo Failed to stop the previous development processes. New processes were not started.
    exit /b 1
)

timeout /t 1 /nobreak >nul

echo === Launching Server (dotnet watch on port 5114) ===
start "RoguelikeServer" cmd /k "cd /d %ROOT% && set ASPNETCORE_ENVIRONMENT=Development&& dotnet watch run --project src/Server --no-launch-profile --urls http://127.0.0.1:5114"

echo === Waiting for Server readiness ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\wait-for-http.ps1" -Url "http://127.0.0.1:5114/api/v1/catalog/cards" -TimeoutSeconds 120
if errorlevel 1 (
    echo Server did not become ready. Client was not started.
    echo Check the RoguelikeServer window for the actual build error.
    exit /b 1
)

echo === Launching Client (Vite on port 5173) ===
start "RoguelikeClient" cmd /k "cd /d %ROOT%src\Client && npm run dev -- --open"

echo.
echo Both windows launched.
echo   Server: http://localhost:5114
echo   Client: http://localhost:5173 (browser opens automatically)
echo.
echo To stop: close each window or Ctrl+C in it.
echo To restart: just run dev.bat again.
endlocal
