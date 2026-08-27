@echo off
setlocal
title Verify UNCAD Installation
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode VerifyUser
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo [OK] Installed files are valid.) else (echo [FAILED] Installation verification failed.)
pause
exit /b %RESULT%
