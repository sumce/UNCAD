@echo off
setlocal
title Uninstall UNCAD for Current User
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode UninstallUser
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo [OK] Current-user uninstall completed.) else (echo [FAILED] Uninstall was not completed.)
pause
exit /b %RESULT%
