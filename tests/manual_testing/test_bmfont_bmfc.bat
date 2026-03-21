@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0\..\.."

REM ============================================================================
REM Run the original AngelCode BMFont64 tool with the same .bmfc files
REM that bmfontier uses (tests 1-18, basic features only).
REM
REM BMFont ignores our extension keys, so the same files work in both tools.
REM
REM Requires: bmfont64.exe at the path below (or pass as first argument)
REM ============================================================================

set BMFONT=%1
if "%BMFONT%"=="" set BMFONT=C:\Users\jerem\Downloads\bmfont64_1.14b_beta\bmfont64.exe

if not exist "%BMFONT%" (
    echo BMFont64 not found at: %BMFONT%
    echo.
    echo Usage: test_bmfont_bmfc.bat [path\to\bmfont64.exe]
    exit /b 1
)

if not exist output\bmfont mkdir output\bmfont

echo === Using BMFont64: %BMFONT% ===
echo.
echo  #  Test                              Time
echo --- --------------------------------- --------

REM --- 1 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-01-arial-16.bmfc -o output\bmfont\test-01.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  1  Arial 16px                        !DUR! ms

REM --- 2 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-02-arial-24.bmfc -o output\bmfont\test-02.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  2  Arial 24px                        !DUR! ms

REM --- 3 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-03-arial-32.bmfc -o output\bmfont\test-03.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  3  Arial 32px                        !DUR! ms

REM --- 4 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-04-arial-48.bmfc -o output\bmfont\test-04.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  4  Arial 48px                        !DUR! ms

REM --- 5 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-05-arial-32-bold.bmfc -o output\bmfont\test-05.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  5  Arial 32px bold                   !DUR! ms

REM --- 6 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-06-arial-32-italic.bmfc -o output\bmfont\test-06.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  6  Arial 32px italic                 !DUR! ms

REM --- 7 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-07-arial-32-bold-italic.bmfc -o output\bmfont\test-07.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  7  Arial 32px bold italic            !DUR! ms

REM --- 8 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-08-arial-32-pad2.bmfc -o output\bmfont\test-08.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  8  Arial 32px padding=2              !DUR! ms

REM --- 9 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-09-arial-32-space4.bmfc -o output\bmfont\test-09.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  9  Arial 32px spacing=4              !DUR! ms

REM --- 10 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-10-arial-32-xml.bmfc -o output\bmfont\test-10.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 10  Arial 32px XML format             !DUR! ms

REM --- 11 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-11-arial-32-binary.bmfc -o output\bmfont\test-11.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 11  Arial 32px binary format          !DUR! ms

REM --- 12 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-12-arial-32-tga.bmfc -o output\bmfont\test-12.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 12  Arial 32px TGA texture            !DUR! ms

REM --- 13 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-13-arial-32-dds.bmfc -o output\bmfont\test-13.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 13  Arial 32px DDS texture            !DUR! ms

REM --- 14 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-14-arial-32-extended.bmfc -o output\bmfont\test-14.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 14  Arial 32px extended charset       !DUR! ms

REM --- 15 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-15-times-32.bmfc -o output\bmfont\test-15.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 15  Times New Roman 32px              !DUR! ms

REM --- 16 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-16-consolas-24.bmfc -o output\bmfont\test-16.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 16  Consolas 24px                     !DUR! ms

REM --- 17 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-17-arial-32-mono.bmfc -o output\bmfont\test-17.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 17  Arial 32px mono (no AA)           !DUR! ms

REM --- 18 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
"%BMFONT%" -c tests\manual_testing\bmfc\test-18-arial-32-multipage.bmfc -o output\bmfont\test-18.fnt >nul 2>&1
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 18  Arial 32px multi-page (128x128)   !DUR! ms

echo.
echo --- --------------------------------- --------
echo.
echo Output in: output\bmfont\
echo Compare with bmfontier output in: output\comparison\
echo.
echo === All 18 BMFont tests completed! ===
exit /b 0

:fail
echo.
echo === FAILED ===
exit /b 1
