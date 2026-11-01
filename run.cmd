@echo off
cd /d "%~dp0"
taskkill /im Volumy.exe /f >nul 2>&1
taskkill /im AudioProfiles.exe /f >nul 2>&1
echo Building Volumy...
dotnet build src\Volumy\Volumy.csproj -c Release -p:Platform=x64 > build.log 2>&1
if errorlevel 1 (
  echo.
  echo Build failed. Errors:
  findstr /i /c:" error " build.log
  echo.
  echo Full log saved to build.log - tell Claude to read it.
  pause
  exit /b 1
)
echo Build OK. Starting...
start "" "src\Volumy\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\Volumy.exe"
