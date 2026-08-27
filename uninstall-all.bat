@echo off
setlocal
title Uninstall UNCAD for All Users
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode UninstallAll
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo [OK] All-users uninstall completed.) else (echo [FAILED] Uninstall was not completed.)
pause
exit /b %RESULT%
