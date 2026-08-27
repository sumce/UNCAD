@echo off
chcp 65001 >nul
net session >nul 2>&1
if %errorlevel% neq 0 (
  echo [ERROR] Please run this file as Administrator.
  pause
  exit /b 1
)
set "SRC=%~dp0bundle\UNCAD.bundle"
set "DST=%ProgramData%\Autodesk\ApplicationPlugins\UNCAD.bundle"
if not exist "%SRC%" (echo [ERROR] bundle not found: %SRC% & pause & exit /b 1)
if not exist "%ProgramData%\Autodesk\ApplicationPlugins" mkdir "%ProgramData%\Autodesk\ApplicationPlugins"
xcopy "%SRC%" "%DST%" /E /I /Y >nul
echo.
echo [OK] UNCAD installed for all users.
echo Restart AutoCAD 2022, then type UNADD / UNL / UNQ1 / UNR at the command line.
pause
