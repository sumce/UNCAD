@echo off
chcp 65001 >nul
set "SRC=%~dp0bundle\UNCAD.bundle"
set "DST=%APPDATA%\Autodesk\ApplicationPlugins\UNCAD.bundle"
if not exist "%SRC%" (echo [ERROR] bundle not found: %SRC% & pause & exit /b 1)
if not exist "%APPDATA%\Autodesk\ApplicationPlugins" mkdir "%APPDATA%\Autodesk\ApplicationPlugins"
xcopy "%SRC%" "%DST%" /E /I /Y >nul
echo.
echo [OK] UNCAD installed for current user.
echo Restart AutoCAD 2022, then type UNADD / UNL / UNQ1 / UNR at the command line.
pause
