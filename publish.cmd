@echo off
rem Builds the same files as the GitHub release, locally: dist\Volumy-Setup-<version>.exe and a portable zip.
rem Needs the .NET 8 SDK; the installer step also needs Inno Setup 6 (https://jrsoftware.org/isinfo.php).
cd /d "%~dp0"
set VERSION=%1
if "%VERSION%"=="" set VERSION=1.0.0
echo Publishing Volumy %VERSION%...
dotnet publish src\Volumy\Volumy.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:Version=%VERSION% -o publish\Volumy || goto :fail
if not exist dist mkdir dist
powershell -NoProfile -Command "Compress-Archive -Force -Path publish\Volumy\* -DestinationPath dist\Volumy-%VERSION%-portable-x64.zip" || goto :fail
set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist %ISCC% (
  %ISCC% /DMyAppVersion=%VERSION% installer\Volumy.iss || goto :fail
) else (
  echo Inno Setup not found: skipped the installer, the portable zip is in dist\
)
echo Done. Files are in dist\
pause
exit /b 0
:fail
echo Publish failed.
pause
exit /b 1
