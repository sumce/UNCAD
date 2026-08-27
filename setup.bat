@echo off
setlocal
title UNCAD Setup - UNSIAO Work
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer.ps1" -Mode Menu
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo.
  echo [FAILED] UNCAD Setup exited with code %RESULT%.
  pause
)
exit /b %RESULT%
