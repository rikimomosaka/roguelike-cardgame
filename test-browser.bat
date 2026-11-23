@echo off
setlocal
set "ROOT=%~dp0"

cd /d "%ROOT%src\Client"
if not exist node_modules\@playwright\test (
    echo === Installing client dependencies ===
    call npm install
    if errorlevel 1 exit /b 1
)

set "PLAYWRIGHT_BROWSER_CHANNEL=chrome"
if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" goto browser_ready
if exist "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" goto browser_ready
if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" (
    set "PLAYWRIGHT_BROWSER_CHANNEL=msedge"
    goto browser_ready
)

echo === No system Chrome or Edge found; installing Playwright Chromium ===
set "PLAYWRIGHT_BROWSER_CHANNEL=chromium"
call npx playwright install chromium
if errorlevel 1 exit /b 1

:browser_ready
echo === Auditing raster asset boundaries ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\AuditRasterAssets.ps1"
if errorlevel 1 exit /b 1

echo === Running isolated browser E2E on Server 5115 / Client 5174 ===
call npm run test:e2e
set "RESULT=%errorlevel%"

if "%RESULT%"=="0" (
    echo.
    echo Browser E2E passed.
) else (
    echo.
    echo Browser E2E failed with exit code %RESULT%.
)

exit /b %RESULT%
