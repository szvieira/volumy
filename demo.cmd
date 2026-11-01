@echo off
rem Opens Volumy with sample profiles (Alex, Sam, Jordan, Riley) for screenshots.
rem Your real profiles are not touched. Run run.cmd afterwards to go back to them.
setlocal
set EXE=%~dp0src\Volumy\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Volumy.exe
if not exist "%EXE%" (
  echo Volumy is not built yet. Run run.cmd first.
  pause
  exit /b 1
)
taskkill /f /im Volumy.exe >nul 2>&1
timeout /t 1 /nobreak >nul
start "" "%EXE%" --demo
echo Demo mode started. Click the Volumy icon in the tray.
