@echo off
chcp 65001 >nul
set "D1=%APPDATA%\Autodesk\ApplicationPlugins\UNCAD.bundle"
set "D2=%ProgramData%\Autodesk\ApplicationPlugins\UNCAD.bundle"
if exist "%D1%" (rmdir /S /Q "%D1%" & echo [OK] removed user install)
if exist "%D2%" (rmdir /S /Q "%D2%" & echo [OK] removed all-user install)
echo Restart AutoCAD to apply.
pause
