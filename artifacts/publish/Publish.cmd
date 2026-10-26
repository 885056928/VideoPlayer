@echo off
setlocal
where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 is required. Please install it and try again.
    pause
    exit /b 1
)
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish.ps1"
set "publishExitCode=%errorlevel%"
if not "%publishExitCode%"=="0" echo Publish failed. Exit code: %publishExitCode%
pause
exit /b %publishExitCode%
