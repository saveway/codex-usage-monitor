@echo off
set "SCRIPT_DIR=%~dp0"
set "RUNTIME_DIR=%LOCALAPPDATA%\CodexUsageMonitor"
if not exist "%RUNTIME_DIR%" mkdir "%RUNTIME_DIR%"
set "LOG_FILE=%RUNTIME_DIR%\codex-usage-tray.log"
set "SCRAPER_LOG=%RUNTIME_DIR%\codex-usage-scraper.log"
echo [%date% %time%] Debug launch started.>>"%LOG_FILE%"
echo [%date% %time%] Debug scraper launch started.>>"%SCRAPER_LOG%"
if exist "%SCRIPT_DIR%codex-usage-scraper.exe" (
    start "Codex usage scraper debug" "%SCRIPT_DIR%codex-usage-scraper.exe"
) else (
    start "Codex usage scraper debug" python "%SCRIPT_DIR%codex-usage-scraper.py"
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%SCRIPT_DIR%codex-usage-tray.ps1" >>"%LOG_FILE%" 2>>&1
echo [%date% %time%] Debug process exited with code %ERRORLEVEL%.>>"%LOG_FILE%"
echo.
echo Log file:
echo %LOG_FILE%
echo.
type "%LOG_FILE%"
pause
