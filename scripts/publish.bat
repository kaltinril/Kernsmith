@echo off
if "%~1"=="" (
    echo Usage: publish.bat ^<version^>
    echo Example: publish.bat 0.9.3
    echo.
    echo This creates a git tag and pushes it, triggering the publish workflow.
    echo Make sure you've already merged a PR that bumps the version in Directory.Build.props.
    exit /b 1
)

echo Verifying on main branch...
for /f %%b in ('git rev-parse --abbrev-ref HEAD') do (
    if not "%%b"=="main" (
        echo ERROR: Must be on main branch. Currently on %%b
        exit /b 1
    )
)

echo Creating tag v%1...
git tag v%1
if errorlevel 1 (
    echo ERROR: Tag v%1 may already exist.
    exit /b 1
)

echo Pushing tag...
git push origin v%1

echo.
echo Done! The GitHub Actions workflow will handle the rest:
echo   - Build CLI and UI binaries for all platforms
echo   - Publish NuGet package
echo   - Create GitHub Release with downloadable binaries
