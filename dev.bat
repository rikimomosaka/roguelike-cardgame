@echo off
REM Roguelike Card Game - dev environment launcher.
REM Run dev.bat (or double-click) to start Server + Client in separate windows.
REM Server uses dotnet watch (auto-rebuild on .cs changes).
REM Client uses Vite hot reload.
REM Re-run dev.bat to restart (auto-kills existing processes on port 5114 / 5173).

setlocal
set ROOT=%~dp0

echo === Stopping existing processes on ports 5114 / 5173 ===
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":5114 " ^| findstr LISTENING') do (
    echo Killing Server PID %%P
    taskkill /F /PID %%P 2>nul
)
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":5173 " ^| findstr LISTENING') do (
    echo Killing Client PID %%P
    taskkill /F /PID %%P 2>nul
)

timeout /t 1 /nobreak >nul

echo === Launching Server (dotnet watch on port 5114) ===
start "RoguelikeServer" cmd /k "cd /d %ROOT% && dotnet watch run --project src/Server"

echo === Launching Client (Vite on port 5173) ===
start "RoguelikeClient" cmd /k "cd /d %ROOT%src\Client && npm run dev"

echo.
echo Both windows launched.
echo   Server: http://localhost:5114
echo   Client: http://localhost:5173
echo.
echo To stop: close each window or Ctrl+C in it.
echo To restart: just run dev.bat again.
endlocal
