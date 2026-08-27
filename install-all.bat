@echo off
setlocal
title Install UNCAD for All Users
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode InstallAll
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo [OK] All-users installation and verification completed.) else (echo [FAILED] All-users installation was not completed.)
pause
exit /b %RESULT%
