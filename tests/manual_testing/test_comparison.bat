@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0\..\.."

REM ============================================================================
REM BMFont vs bmfontier comparison tests
REM
REM These use only features available in the original AngelCode BMFont tool
REM so you can generate equivalent output from both and compare side-by-side.
REM
REM For each test, the equivalent BMFont settings are noted in comments.
REM ============================================================================

echo === Building CLI (Release) ===
dotnet build tools\Bmfontier.Cli -c Release --nologo -v minimal
if errorlevel 1 goto :fail

if not exist output\comparison mkdir output\comparison

set EXE=tools\Bmfontier.Cli\bin\Release\net10.0\Bmfontier.Cli.exe

REM Capture overall start time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "TOTAL_START=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"

echo.
echo  #  Test                              Time
echo --- --------------------------------- --------

REM --- 1 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 16px, ASCII, 256x256 texture, text output
%EXE% generate --system-font "Arial" -s 16 --charset ascii --format text --max-texture-size 256 -o output/comparison/arial-16 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  1  Arial 16px                        !DUR! ms

REM --- 2 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 24px, ASCII, 512x512 texture, text output
%EXE% generate --system-font "Arial" -s 24 --charset ascii --format text --max-texture-size 512 -o output/comparison/arial-24 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  2  Arial 24px                        !DUR! ms

REM --- 3 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, ASCII, 512x512 texture, text output
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --max-texture-size 512 -o output/comparison/arial-32 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  3  Arial 32px                        !DUR! ms

REM --- 4 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 48px, ASCII, 1024x1024 texture, text output
%EXE% generate --system-font "Arial" -s 48 --charset ascii --format text --max-texture-size 1024 -o output/comparison/arial-48 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  4  Arial 48px                        !DUR! ms

REM --- 5 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial Bold, 32px, ASCII, 512x512 texture
%EXE% generate --system-font "Arial" -s 32 -b --charset ascii --format text --max-texture-size 512 -o output/comparison/arial-32-bold >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  5  Arial 32px bold                   !DUR! ms

REM --- 6 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial Italic, 32px, ASCII, 512x512 texture
%EXE% generate --system-font "Arial" -s 32 -i --charset ascii --format text --max-texture-size 512 -o output/comparison/arial-32-italic >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  6  Arial 32px italic                 !DUR! ms

REM --- 7 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial Bold Italic, 32px, ASCII, 512x512 texture
%EXE% generate --system-font "Arial" -s 32 -b -i --charset ascii --format text --max-texture-size 512 -o output/comparison/arial-32-bold-italic >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  7  Arial 32px bold italic            !DUR! ms

REM --- 8 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, padding up=2 right=2 down=2 left=2
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --padding 2 --max-texture-size 512 -o output/comparison/arial-32-pad2 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  8  Arial 32px padding=2              !DUR! ms

REM --- 9 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, spacing horiz=4 vert=4
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --spacing 4 --max-texture-size 512 -o output/comparison/arial-32-space4 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo  9  Arial 32px spacing=4              !DUR! ms

REM --- 10 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, XML descriptor output
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format xml --max-texture-size 512 -o output/comparison/arial-32-xml >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 10  Arial 32px XML format             !DUR! ms

REM --- 11 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, binary descriptor output
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format binary --max-texture-size 512 -o output/comparison/arial-32-binary >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 11  Arial 32px binary format          !DUR! ms

REM --- 12 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, TGA output (32-bit)
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --texture-format tga --max-texture-size 512 -o output/comparison/arial-32-tga >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 12  Arial 32px TGA texture            !DUR! ms

REM --- 13 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, DDS output
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --texture-format dds --max-texture-size 512 -o output/comparison/arial-32-dds >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 13  Arial 32px DDS texture            !DUR! ms

REM --- 14 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, characters 32-255 (Latin-1 Supplement)
%EXE% generate --system-font "Arial" -s 32 --charset extended --format text --max-texture-size 1024 -o output/comparison/arial-32-extended >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 14  Arial 32px extended charset       !DUR! ms

REM --- 15 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Times New Roman, 32px, ASCII
%EXE% generate --system-font "Times New Roman" -s 32 --charset ascii --format text --max-texture-size 512 -o output/comparison/times-32 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 15  Times New Roman 32px              !DUR! ms

REM --- 16 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Consolas, 24px, ASCII
%EXE% generate --system-font "Consolas" -s 24 --charset ascii --format text --max-texture-size 256 -o output/comparison/consolas-24 >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 16  Consolas 24px                     !DUR! ms

REM --- 17 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, smooth=none
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --mono --max-texture-size 512 -o output/comparison/arial-32-mono >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 17  Arial 32px mono (no AA)           !DUR! ms

REM --- 18 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
REM BMFont: Arial, 32px, 128x128 texture (forces multiple pages)
%EXE% generate --system-font "Arial" -s 32 --charset ascii --format text --max-texture-size 128 -o output/comparison/arial-32-multipage >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 18  Arial 32px multi-page (128x128)   !DUR! ms

echo.
echo --- --------------------------------- --------

REM --- Batch mode ---
echo.
echo === Batch mode (all 18 via single invocation) ===
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% batch tests\manual_testing\bmfc\*.bmfc --time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo     Batch total:                      !DUR! ms

REM Capture overall end time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "TOTAL_END=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "TOTAL_DUR=(TOTAL_END-TOTAL_START)*10"
echo     TOTAL (18 generations)            !TOTAL_DUR! ms

echo.
echo === All 18 comparison tests passed! ===
exit /b 0

:fail
echo.
echo === FAILED (exit code %errorlevel%) ===
exit /b 1
