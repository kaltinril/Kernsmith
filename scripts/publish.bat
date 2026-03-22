@echo off
if "%~1"=="" (
    echo Usage: publish.bat ^<version^>
    echo Example: publish.bat 0.9.2
    exit /b 1
)

echo Updating Directory.Build.props to version %1...
powershell -Command "(Get-Content Directory.Build.props) -replace '<Version>[^<]*</Version>', '<Version>%1</Version>' | Set-Content Directory.Build.props"

echo Committing version bump...
git commit -am "Bump version to %1"

echo Creating tag v%1...
git tag v%1

echo Pushing commit and tag...
git push && git push origin v%1

echo.
echo Done! The GitHub Actions workflow will handle the rest:
echo   - Build CLI and UI binaries for all platforms
echo   - Publish NuGet package
echo   - Create GitHub Release with downloadable binaries
