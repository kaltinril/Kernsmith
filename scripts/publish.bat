@echo off
for /f "usebackq tokens=1,* delims==" %%a in ("scripts\.env") do (
    if "%%a"=="NUGET_API_KEY" set NUGET_API_KEY=%%b
)
dotnet pack src/KernSmith/KernSmith.csproj --configuration Release --output ./nupkg
dotnet nuget push ./nupkg/KernSmith.*.nupkg --api-key %NUGET_API_KEY% --source https://api.nuget.org/v3/index.json
