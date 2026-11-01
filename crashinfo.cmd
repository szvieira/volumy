@echo off
cd /d "%~dp0"
echo Reading Windows crash reports for the last 3 hours...
wevtutil qe Application /q:"*[System[Provider[@Name='Application Error' or @Name='.NET Runtime' or @Name='Windows Error Reporting'] and TimeCreated[timediff(@SystemTime) <= 10800000]]]" /f:text /rd:true /c:15 > crashinfo.txt 2>&1
echo.
echo Saved to crashinfo.txt - tell Claude to read it.
pause
