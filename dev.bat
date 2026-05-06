@echo off
REM Roguelike Card Game — dev environment launcher
REM ダブルクリック (or プロンプトで `dev`) で Server + Client 両方を別ウィンドウで起動。
REM Server は dotnet watch 経由なので C# 変更で自動リビルド (再起動不要)。
REM Client は Vite hot reload (TS/TSX 変更で自動リロード)。
REM
REM 既存の dev server が動いていればまず port (5114 / 5173) で kill してから起動する。
REM すべてのプロセスを止めたい時は各ウィンドウで Ctrl+C → ウィンドウを閉じる。

setlocal
set ROOT=%~dp0

echo === Stopping any process on port 5114 (Server) / 5173 (Client) ===
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":5114 " ^| findstr LISTENING') do (
    echo Killing Server PID %%P
    taskkill /F /PID %%P 2>nul
)
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":5173 " ^| findstr LISTENING') do (
    echo Killing Client PID %%P
    taskkill /F /PID %%P 2>nul
)

REM ファイルロック解放まで一瞬待つ
timeout /t 1 /nobreak >nul

echo === Launching Server (dotnet watch on port 5114) ===
start "Roguelike Server" cmd /k "cd /d %ROOT% && dotnet watch run --project src/Server"

echo === Launching Client (Vite on port 5173) ===
start "Roguelike Client" cmd /k "cd /d %ROOT%src\Client && npm run dev"

echo.
echo Both windows launched.
echo   Server: http://localhost:5114
echo   Client: http://localhost:5173
echo.
echo To stop: close each window or Ctrl+C in it.
echo To restart: just run dev.bat again (it auto-kills the old ones first).
endlocal
