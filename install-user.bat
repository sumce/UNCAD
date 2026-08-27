@echo off
setlocal
title Install UNCAD for Current User
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode InstallUser
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo [OK] Installation and verification completed.) else (echo [FAILED] Installation was not completed.)
pause
exit /b %RESULT%
