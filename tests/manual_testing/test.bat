@echo off
setlocal
cd /d "%~dp0\..\.."

echo === Building CLI ===
dotnet build tools\Bmfontier.Cli -c Release --nologo -v minimal
if errorlevel 1 goto :fail

if not exist output mkdir output

set EXE=tools\Bmfontier.Cli\bin\Release\net10.0\Bmfontier.Cli.exe

echo.
echo === 1. Neon Cyberpunk — pink-to-cyan gradient, dark outline ===
%EXE% generate --system-font "Impact" -s 64 -b --outline 3,0D0D0D --gradient FF00FF,00FFFF --gradient-angle 90 --charset ascii --autofit --padding 2 -o output/neon-cyberpunk
if errorlevel 1 goto :fail

echo.
echo === 2. Fire Text — red-to-gold gradient, black outline, shadow ===
%EXE% generate --system-font "Georgia" -s 56 -b --outline 4,1A0500 --gradient FF0000,FFD700 --gradient-angle 0 --shadow 3,3,000000,3 --charset ascii --autofit -o output/fire-text
if errorlevel 1 goto :fail

echo.
echo === 3. Ocean Depths — teal-to-navy, thick outline, italic ===
%EXE% generate --system-font "Times New Roman" -s 48 -b -i --outline 5,001122 --gradient 00CED1,000080 --gradient-angle 180 --charset latin --autofit --super-sample 2 --padding 3 -o output/ocean-depths
if errorlevel 1 goto :fail

echo.
echo === 4. Retro Arcade — green monospace, channel-packed into RGBA ===
%EXE% generate --system-font "Consolas" -s 32 --charset ascii --autofit --channel-pack --no-hinting --spacing 2 -o output/retro-arcade
if errorlevel 1 goto :fail

echo.
echo === 5. Sunset Glow — orange-to-purple, angled gradient, super-sampled ===
%EXE% generate --system-font "Arial" -s 52 -b --outline 3,2B0040 --gradient FF6B35,9B59B6 --gradient-angle 135 --gradient-midpoint 0.4 --charset ascii --autofit --super-sample 4 --padding 4 -o output/sunset-glow
if errorlevel 1 goto :fail

echo.
echo === 6. Ice Crystal — white-to-blue, SDF, large ===
%EXE% generate --system-font "Verdana" -s 72 --outline 2,1A237E --gradient FFFFFF,42A5F5 --gradient-angle 0 --sdf --charset ascii --autofit --padding 6 -o output/ice-crystal
if errorlevel 1 goto :fail

echo.
echo === 7. Toxic Slime — lime-to-yellow, fat outline, skyline packer ===
%EXE% generate --system-font "Impact" -s 60 -b --outline 6,1B0030 --gradient 76FF03,FFFF00 --gradient-angle 45 --shadow 4,4,000000,4 --charset ascii --autofit --packer skyline --padding 4 --spacing 3 -o output/toxic-slime
if errorlevel 1 goto :fail

echo.
echo === 8. Royal Gold — gold-to-bronze, serif, XML format ===
%EXE% generate --system-font "Georgia" -s 44 -b --outline 3,2C1810 --gradient FFD700,CD853F --gradient-angle 0 --gradient-midpoint 0.6 --charset extended --autofit --format xml --super-sample 2 -o output/royal-gold
if errorlevel 1 goto :fail

echo.
echo === 9. Synthwave — hot pink-to-violet, bold italic, binary format ===
%EXE% generate --system-font "Arial" -s 50 -b -i --outline 4,0A001A --gradient FF1493,8A2BE2 --gradient-angle 90 --shadow 2,2,330033,2 --charset ascii --autofit --format binary --padding 3 -o output/synthwave
if errorlevel 1 goto :fail

echo.
echo === 10. Terminal Hacker — green monospace, no AA, TGA format ===
%EXE% generate --system-font "Courier New" -s 28 --gradient 00FF41,00CC33 --charset ascii --autofit --mono --texture-format tga --spacing 1 -o output/terminal-hacker
if errorlevel 1 goto :fail

echo.
echo === 11. Blood Moon — dark red-to-crimson, big shadow, 256px texture ===
%EXE% generate --system-font "Times New Roman" -s 40 -b --outline 3,0A0000 --gradient 8B0000,DC143C --gradient-angle 180 --shadow 5,5,000000,5 --charset ascii --max-texture-size 256 --padding 2 -o output/blood-moon
if errorlevel 1 goto :fail

echo.
echo === 12. Pastel Dream — soft pink-to-lavender, light touch ===
%EXE% generate --system-font "Verdana" -s 36 --outline 1,E8D5E0 --gradient FFB6C1,E6E6FA --gradient-angle 45 --charset latin --autofit --super-sample 2 --height-percent 110 -o output/pastel-dream
if errorlevel 1 goto :fail

echo.
echo === 13. Minecraft Style — tiny, no hinting, POT, DDS format ===
%EXE% generate --system-font "Consolas" -s 16 --charset ascii --autofit --no-hinting --pot --texture-format dds --padding 1 --spacing 1 --equalize-heights -o output/minecraft-style
if errorlevel 1 goto :fail

echo.
echo === 14. Galaxy Swirl — blue-to-magenta, all the bells and whistles ===
%EXE% generate --system-font "Georgia" -s 64 -b -i --outline 5,0D001A --gradient 4169E1,FF00FF --gradient-angle 120 --gradient-midpoint 0.3 --shadow 4,4,000000,3 --charset extended --autofit --super-sample 4 --packer skyline --padding 5 --spacing 3 --fallback-char ? -o output/galaxy-swirl
if errorlevel 1 goto :fail

echo.
echo === 15. Inspect outputs (text, xml, binary) ===
%EXE% inspect output/neon-cyberpunk.fnt
if errorlevel 1 goto :fail
%EXE% inspect output/royal-gold.fnt
if errorlevel 1 goto :fail
%EXE% inspect output/synthwave.fnt
if errorlevel 1 goto :fail

echo.
echo === All 14 generations + inspects passed! ===
exit /b 0

:fail
echo.
echo === FAILED (exit code %errorlevel%) ===
exit /b 1
