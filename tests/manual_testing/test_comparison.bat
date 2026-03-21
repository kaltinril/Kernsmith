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

REM Capture basic section end time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "BASIC_END=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "BASIC_DUR=(BASIC_END-TOTAL_START)*10"
echo     BASIC TOTAL (18 tests)            !BASIC_DUR! ms

echo.
echo ===================================================================
echo  ADVANCED FEATURES (gradients, outlines, shadows, SDF, effects)
echo ===================================================================
echo.
echo  #  Test                              Time
echo --- --------------------------------- --------

REM --- 19 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Impact" -s 64 -b --outline 3,0D0D0D --gradient FF00FF,00FFFF --gradient-angle 90 --charset ascii --autofit --padding 2 -o output/comparison/neon-cyberpunk >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 19  Neon Cyberpunk (outline+gradient)  !DUR! ms

REM --- 20 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Georgia" -s 56 -b --outline 4,1A0500 --gradient FF0000,FFD700 --gradient-angle 0 --shadow 3,3,000000,3 --charset ascii --autofit -o output/comparison/fire-text >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 20  Fire Text (outline+grad+shadow)    !DUR! ms

REM --- 21 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Times New Roman" -s 48 -b -i --outline 5,001122 --gradient 00CED1,000080 --gradient-angle 180 --charset latin --autofit --super-sample 2 --padding 3 -o output/comparison/ocean-depths >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 21  Ocean Depths (outline+grad+ss2)    !DUR! ms

REM --- 22 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Consolas" -s 32 --charset ascii --autofit --channel-pack --no-hinting --spacing 2 -o output/comparison/retro-arcade >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 22  Retro Arcade (channel-pack)        !DUR! ms

REM --- 23 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Arial" -s 52 -b --outline 3,2B0040 --gradient FF6B35,9B59B6 --gradient-angle 135 --gradient-midpoint 0.4 --charset ascii --autofit --super-sample 4 --padding 4 -o output/comparison/sunset-glow >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 23  Sunset Glow (outline+grad+ss4)     !DUR! ms

REM --- 24 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Verdana" -s 72 --outline 2,1A237E --gradient FFFFFF,42A5F5 --gradient-angle 0 --sdf --charset ascii --autofit --padding 6 -o output/comparison/ice-crystal >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 24  Ice Crystal (SDF+outline+grad)     !DUR! ms

REM --- 25 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Impact" -s 60 -b --outline 6,1B0030 --gradient 76FF03,FFFF00 --gradient-angle 45 --shadow 4,4,000000,4 --charset ascii --autofit --packer skyline --padding 4 --spacing 3 -o output/comparison/toxic-slime >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 25  Toxic Slime (outline+grad+shadow)  !DUR! ms

REM --- 26 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Georgia" -s 44 -b --outline 3,2C1810 --gradient FFD700,CD853F --gradient-angle 0 --gradient-midpoint 0.6 --charset extended --autofit --format xml --super-sample 2 -o output/comparison/royal-gold >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 26  Royal Gold (XML+outline+grad+ss2)  !DUR! ms

REM --- 27 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Arial" -s 50 -b -i --outline 4,0A001A --gradient FF1493,8A2BE2 --gradient-angle 90 --shadow 2,2,330033,2 --charset ascii --autofit --format binary --padding 3 -o output/comparison/synthwave >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 27  Synthwave (binary+outline+shadow)  !DUR! ms

REM --- 28 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Courier New" -s 28 --gradient 00FF41,00CC33 --charset ascii --autofit --mono --texture-format tga --spacing 1 -o output/comparison/terminal-hacker >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 28  Terminal Hacker (TGA+grad+mono)    !DUR! ms

REM --- 29 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Times New Roman" -s 40 -b --outline 3,0A0000 --gradient 8B0000,DC143C --gradient-angle 180 --shadow 5,5,000000,5 --charset ascii --max-texture-size 256 --padding 2 -o output/comparison/blood-moon >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 29  Blood Moon (256px+outline+shadow)  !DUR! ms

REM --- 30 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Verdana" -s 36 --outline 1,E8D5E0 --gradient FFB6C1,E6E6FA --gradient-angle 45 --charset latin --autofit --super-sample 2 --height-percent 110 -o output/comparison/pastel-dream >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 30  Pastel Dream (latin+grad+ss2)      !DUR! ms

REM --- 31 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Consolas" -s 16 --charset ascii --autofit --no-hinting --pot --texture-format dds --padding 1 --spacing 1 --equalize-heights -o output/comparison/minecraft-style >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 31  Minecraft (DDS+nohint+equalize)    !DUR! ms

REM --- 32 ---
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% generate --system-font "Georgia" -s 64 -b -i --outline 5,0D001A --gradient 4169E1,FF00FF --gradient-angle 120 --gradient-midpoint 0.3 --shadow 4,4,000000,3 --charset extended --autofit --super-sample 4 --packer skyline --padding 5 --spacing 3 --fallback-char ? -o output/comparison/galaxy-swirl >nul
if errorlevel 1 goto :fail
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo 32  Galaxy Swirl (everything+ss4)      !DUR! ms

echo.
echo --- --------------------------------- --------

REM Capture advanced section end time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "ADV_END=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "ADV_DUR=(ADV_END-BASIC_END)*10"
echo     ADVANCED TOTAL (14 tests)          !ADV_DUR! ms

echo.
echo ===================================================================
echo  BATCH MODE (all 32 via single invocation)
echo ===================================================================
echo.
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T0=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
%EXE% batch tests\manual_testing\bmfc\*.bmfc --time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "T1=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "DUR=(T1-T0)*10"
echo     Batch total:                       !DUR! ms

echo.
echo ===================================================================
echo  SUMMARY
echo ===================================================================
echo.

REM Capture overall end time
for /f "tokens=1-4 delims=:." %%a in ("%TIME: =0%") do set /a "TOTAL_END=(((%%a*60+1%%b%%100)*60+1%%c%%100)*100+1%%d%%100)"
set /a "TOTAL_DUR=(TOTAL_END-TOTAL_START)*10"
echo  Basic tests (1-18):                   !BASIC_DUR! ms
echo  Advanced tests (19-32):               !ADV_DUR! ms
echo  Batch mode (all 32):                  !DUR! ms
echo  GRAND TOTAL:                          !TOTAL_DUR! ms

echo.
echo === All 32 tests + batch passed! ===
exit /b 0

:fail
echo.
echo === FAILED (exit code %errorlevel%) ===
exit /b 1
